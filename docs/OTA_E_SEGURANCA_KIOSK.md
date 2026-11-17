# Arquitetura de Atualização OTA sem UAC, Segurança Kiosk e Implantação Offline

Este documento registra a arquitetura definitiva, as causas de falhas históricas resolvidas na **`v1.0.9.4` / `v1.0.9.5`** e as regras obrigatórias para que nenhuma atualização futura quebre o sistema nas máquinas das unidades APAC.

---

## 1. Qual arquivo enviar para instalação nas máquinas (ponto de partida)

> [!IMPORTANT]
> **Arquivo oficial para implantação inicial / atualização manual das máquinas:**
> - **`Instalador_ApacKiosk_v1.0.9.5.exe`** (ou o instalador offline da release mais recente `>= v1.0.9.5`)
> - Download oficial na Release: `https://github.com/joemcestrategia/apac/releases/download/v1.0.9.5/Instalador_ApacKiosk_v1.0.9.5.exe`
> - Caminho local gerado no repositório: `publish/Instalador_ApacKiosk_v1.0.9.5.exe`
> - SHA-256 (`v1.0.9.5`): `763E6B59E1793C81355A22F15F889BEBE1AC5573B6431D227D34BB826187C331`

### Por que máquinas antigas (`<= v1.0.9.3`) precisam deste instalador offline uma vez?
1. Versões até `v1.0.9.2`/`v1.0.9.3` possuíam o `ProcessWatcher` encerrando o próprio atualizador em `%TEMP%` (`ApacKiosk_vX.Y.Z.W.exe`), além de chaves duplicadas de autostart no Registro e ausência do comando de políticas `Ctrl+Alt+Del` no serviço `LocalSystem`. Como quem baixa e lança o novo `.exe` durante um OTA é o processo **já instalado** na máquina, versões antigas abatem o atualizador antes da troca.
2. Ao executar **`Instalador_ApacKiosk_v1.0.9.5.exe`** uma única vez em cada máquina (com UAC de instalação manual):
   - Instala `ApacKiosk.exe` e `ApacUpdaterHost.exe` na versão `v1.0.9.5` em `C:\Program Files\APAC Kiosk Guardian\`;
   - Registra e inicia o serviço do Windows **`ApacKioskUpdater`** rodando como **`LocalSystem`** (`"C:\Program Files\APAC Kiosk Guardian\ApacUpdaterHost.exe" --update-service`);
   - Unifica a inicialização automática para **apenas uma entrada oficial** (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\APAC Kiosk Guardian`), removendo chaves duplicadas legadas (`ApacKioskGuardian` e `APAC_Kiosk`);
   - Limpa quaisquer políticas residuais antigas no Registro e ajusta permissões de `C:\ProgramData\ApacKiosk`.
3. **A partir da `v1.0.9.4` / `v1.0.9.5`**, as máquinas **não precisam mais de instalador manual nem de permissão UAC** para receber atualizações: todo update futuro publicado via `scripts/publish_ota.ps1` é baixado, validado por SHA-256, instalado via serviço `ApacKioskUpdater` em tela bloqueada e reiniciado automaticamente.

---

## 2. Como o fluxo OTA sem UAC funciona ponta a ponta

1. **Verificação e download (`UpdateManager`):**
   - No boot (`SplashScreen`) ou sob demanda (`UpdateStatusDialog`), consulta `https://api.github.com/repos/joemcestrategia/apac/releases/latest` (com fallback por redirect web caso haja HTTP 403/429).
   - Baixa `ApacKiosk.exe` para `%TEMP%\ApacUpdates\ApacKiosk_v<versao>.exe.part`, confere tamanho + SHA-256 com `ApacKiosk.exe.sha256` e move para `ApacKiosk_v<versao>.exe`.
2. **Transferência segura de tela (`ApplyAtomicUpdate` -> `--service-update-ui`):**
   - `Program.ProcessWatcher?.Pause()` e `RegisterAllowedProcessName` são chamados **antes** de `Process.Start` do pacote em `%TEMP%`.
   - O pacote novo inicia em `--service-update-ui`, adquire o mutex `Global\APACKioskGuardian_UpdateInProgress` (impedindo instâncias concorrentes do `Run` ou atalho durante a troca), abre `UpdaterStatusForm` maximizado/TopMost sem exposição do desktop (`ProtectRegistry: false`) e grava `ui_ready_<token>.flag`.
   - O processo antigo só chama `PrepareForServiceUiTransition()` (`ProcessWatcher.Stop()`, `ShutdownPreservingLock()`, libera `SingleInstance`) e sai (`Environment.Exit(0)`) **depois** de confirmar `ui_ready_<token>.flag`.
   - Se o `--service-update-ui` falhar ou encerrar antes de gravar `readyFlag` (ou atingir timeout de 15s), o processo antigo **aborta o encerramento**, retoma `ProcessWatcher` e mantém o quiosque travado no sistema (nunca abandona a sessão nem deixa a barra oculta).
3. **Aplicação privilegiada sem UAC (`WindowsUpdaterService` / `ApacKioskUpdaterPipe`):**
   - `UpdaterStatusForm` envia comando `APPLY` via Named Pipe `ApacKioskUpdaterPipe` ao serviço Windows `ApacKioskUpdater` (`LocalSystem`).
   - O serviço copia o pacote para área protegida `C:\ProgramData\ApacKiosk\ServiceStaging` (acesso restrito a `SYSTEM`/`Administrators`), recalcula SHA-256 e confere contra a release oficial no GitHub (`VerifyOfficialGitHubReleaseAsync`).
   - Faz backup `ApacKiosk.old.exe`, substitui `C:\Program Files\APAC Kiosk Guardian\ApacKiosk.exe`, verifica hash no destino e garante permissão `Builtin\Users` ReadAndExecute (`EnsureTargetBinaryReadableByUsers`).
4. **Validação de boot saudável, Rollback e Refresh do host (`LaunchTargetAndWaitBootConfirmation`):**
   - Lança `ApacKiosk.exe --cleanup-updates --wait-pid=<uiPid> --boot-token=<token>`.
   - A nova versão grava `boot_ok_<token>.flag` em `Program.Main` logo após adquirir `Global\APACKioskGuardian_SingleInstance`.
   - Se a nova versão encerrar antes de confirmar `boot_ok` com código != 0, o updater aciona `ROLLBACK` no serviço e restaura `ApacKiosk.old.exe`.
   - Com `boot_ok` confirmado, chama `CLEANUP_BACKUP`, que remove `ApacKiosk.old.exe` e executa `ScheduleHostSelfRefreshIfNeeded` para atualizar `ApacUpdaterHost.exe` para a versão recém-instalada.

---

## 3. Como a blindagem de tela, `Ctrl+Alt+Del` e barra de tarefas funciona

1. **Por que o serviço `LocalSystem` controla políticas do Registro:**
   - Em Windows 10/11, `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies` possui apenas `ReadKey` para usuários padrão (sem UAC), permitindo escrita somente a `SYSTEM` e `Administrators`.
   - Por isso `WindowsUpdaterService` implementa `"SET_CTRL_ALT_DEL_LOCK"`, usando `server.RunAsClient(...)` para descobrir com autenticidade pelo kernel o SID do usuário conectado no pipe, gravando/removendo em `HKU\<SID>\Software\Microsoft\Windows\CurrentVersion\Policies\System` (`DisableTaskMgr`, `DisableLockWorkstation`, `DisableChangePassword`), `Policies\Explorer` (`NoClose`, `NoLogoff`) e `HKLM\...\Policies\System` (`HideFastUserSwitching`).
2. **Invariante de reversibilidade (Barra de Tarefas e Políticas):**
   - `ShutdownPreservingLock()` mantém barra oculta (`Shell_TrayWnd`) e políticas ativas **exclusivamente** durante uma transição validada de atualização.
   - `DisableAllSecurity()` remove **todas** as 6 políticas de bloqueio e restaura `Shell_TrayWnd`, `Shell_SecondaryTrayWnd` e ícones da área de trabalho ao:
     * entrar no `AdminDashboard`;
     * fechar o quiosque com a senha de administrador (`LoginForm.AuthorizedShutdown` / `KioskDesktopForm`);
     * acionar o Modo Emergência;
     * desinstalar (`UninstallHelper.ExecuteFullUninstall` chama `RestoreSystemShellAndPolicies(includeService: true)` antes de remover o serviço) ou finalizar `UpdaterStatusForm` sem handover.
3. **Fechamento protegido:**
   - `Program.Main` só encerra o loop de login se `LoginForm.AuthorizedShutdown == true` (senha BCrypt de administrador validada).
   - `SplashScreen`, `LoginForm` e `KioskDesktopForm` bloqueiam `CloseReason.WindowsShutDown` e `CloseReason.TaskManagerClosing`.
4. **Autostart único (`AutostartManager`):**
   - Única chave oficial: `"APAC Kiosk Guardian"` em `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` (fallback `HKCU` apenas se sem acesso a `HKLM` e nenhuma entrada `HKLM` válida existir).
   - Chaves legadas (`ApacKioskGuardian`, `APAC_Kiosk`) são limpas automaticamente na inicialização, no `SystemConfigTab`, no `RegistryProtector`, no instalador e no desinstalador.

---

## 4. Regras invioláveis para futuras alterações no código

1. **Nunca** iniciar processo de atualização/externo antes de chamar `Program.ProcessWatcher?.Pause()` e `RegisterAllowedProcessName` / `RegisterAllowedProcess`.
2. **Nunca** remover `userinit.exe`, `ApacUpdaterHost.exe`, `ApacHostRefresher.exe`, `ApacKioskUninstall.exe` de `ProcessWatcher._neverKill` nem remover a proteção que ignora processos de `SessionId == 0`.
3. **Nunca** encerrar o processo principal do Kiosk durante uma transição sem confirmar `ui_ready_<token>.flag`; em qualquer falha antes do handover, retomar `ProcessWatcher` e permanecer bloqueado.
4. **Nunca** adicionar chamadas diretas a `HKLM\...\Run` ou `HKCU\...\Run` fora de `AutostartManager` / `InstallerForm.RegisterAutoStart`.
5. **Nunca** deixar políticas `Policies\System` ou `Policies\Explorer` ativas sem limpeza garantida em `DisableAllSecurity()`.
6. **Nunca** exibir caixas de logs técnicos na UI — registrar diagnósticos exclusivamente em `%TEMP%\ApacKiosk_Update.log`, `C:\ProgramData\ApacKiosk\ApacKiosk_Update.log`, `%TEMP%\ApacKiosk_UpdateError.log`, `%TEMP%\ApacKiosk_Trace.log` e `%TEMP%\ApacKiosk_Crash.log`.
7. **Sempre** lançar releases usando `powershell -ExecutionPolicy Bypass -File scripts\publish_ota.ps1` (para gerar build `LocalOnly` de teste sem subir no GitHub, use `-LocalOnly`).

---

## 5. Checklist obrigatório antes de publicar qualquer nova versão OTA

- [ ] `dotnet build ApacKiosk/ApacKiosk.csproj --configuration Release` concluído com **0 erros**.
- [ ] `dotnet build ApacKiosk.Installer/ApacKiosk.Installer.csproj --configuration Release` concluído com **0 erros**.
- [ ] Conferir se novas dependências/processos auxiliares do Kiosk não são bloqueados pelo `ProcessWatcher`.
- [ ] Conferir que `UpdateManager.GetCurrentVersion()` foi incrementado via `publish_ota.ps1` para versão superior à última release publicada no GitHub.
- [ ] Confirmar após rodar `publish_ota.ps1` que a Release no GitHub contém exatamente os 3 assets esperados: `ApacKiosk.exe`, `ApacKiosk.exe.sha256` e `Instalador_ApacKiosk_vX.Y.Z.W.exe`.
- [ ] Em máquina com serviço ativo (`>= v1.0.9.5`), testar abertura como usuário padrão (sem Run as Admin) validando no `C:\ProgramData\ApacKiosk\ApacKiosk_Update.log` as etapas `[Startup] -> [Download] -> [Apply] -> [UpdaterUI] -> [Service] -> [Boot]`.
