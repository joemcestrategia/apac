# PROMPT PARA AGENTE — APAC KIOSK GUARDIAN
**Desenvolvedor:** Dionathan Iannini  
**Sistema:** Windows Kiosk com Controle Total, Monitoramento e Gestão de Usuários  
**Destinado às:** APACs de todo o Brasil

---

## CONTEXTO GERAL

Você irá construir um sistema de kiosk seguro para Windows chamado **APAC Kiosk Guardian**, desenvolvido por **Dionathan Iannini** para uso nas unidades da APAC (Associação de Proteção e Assistência aos Condenados) em todo o Brasil.

O sistema deve assumir controle total do computador ao ser iniciado, permitindo acesso apenas aos sites definidos pelo administrador. Deve ser completamente resistente a fechamento não autorizado, possui monitoramento avançado com logs de screenshots, câmera, teclado, e gestão completa de usuários com regras de tempo de acesso.

---

## STACK TECNOLÓGICA RECOMENDADA

- **Linguagem principal:** C# (.NET 8 WinForms ou WPF)
- **Browser embarcado:** Microsoft WebView2 (Chromium) — NuGet: `Microsoft.Web.WebView2`
- **Banco de dados local:** SQLite via `Microsoft.Data.Sqlite`
- **Captura de tela:** `System.Drawing` / `Graphics.CopyFromScreen`
- **Captura de câmera:** `AForge.NET` ou `DirectShow.NET`
- **Keylogger:** `SetWindowsHookEx` via P/Invoke (WinAPI — `user32.dll`)
- **Bloqueio do sistema:** WinAPI via P/Invoke (`RegisterHotKey`, `SetWindowPos`, Shell hooks)
- **Autostart:** Registro do Windows (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`)
- **Proteção contra Task Manager:** Política de grupo via código + hook de processo
- **Empacotamento/Instalador:** NSIS ou Inno Setup

---

## ARQUITETURA DO PROJETO

```
APACKioskGuardian/
├── Core/
│   ├── KioskEngine.cs          # Controle principal do kiosk
│   ├── SecurityManager.cs      # Proteções do sistema
│   ├── ProcessWatcher.cs       # Mata processos não autorizados
│   └── HotkeyBlocker.cs        # Bloqueia Alt+F4, Win, Ctrl+Alt+Del, etc.
├── Auth/
│   ├── AdminAuth.cs            # Autenticação do admin (senha hash bcrypt)
│   ├── UserAuth.cs             # Login de usuários
│   └── BiometricAuth.cs        # Stub para futura integração biométrica
├── UI/
│   ├── SplashScreen.xaml       # Tela inicial com logo APAC
│   ├── LoginScreen.xaml        # Login usuário / admin
│   ├── AdminPanel.xaml         # Painel de administração completo
│   ├── KioskBrowser.xaml       # Browser WebView2 em tela cheia
│   └── BlockedScreen.xaml      # Tela exibida quando acesso é bloqueado
├── Monitoring/
│   ├── ScreenCapture.cs        # Screenshots periódicos
│   ├── CameraCapture.cs        # Fotos pela webcam
│   ├── KeyLogger.cs            # Registro de teclado
│   └── LogManager.cs           # Centraliza e salva todos os logs
├── Rules/
│   ├── WebFilterEngine.cs      # Filtro de URLs permitidas
│   ├── ScheduleEngine.cs       # Regras de horário por usuário
│   └── SessionManager.cs       # Controle de sessão ativa
└── Data/
    ├── DatabaseService.cs      # Acesso SQLite
    └── Models/                 # Entidades: User, Rule, LogEntry, etc.
```

---

## FUNCIONALIDADES DETALHADAS

### 1. INICIALIZAÇÃO E CONTROLE DO SISTEMA

- Ao iniciar, exibir **SplashScreen** com o logo da APAC centralizado, nome "APAC Kiosk Guardian" e versão
- Aplicar imediatamente as seguintes proteções:
  - Definir a janela como `TopMost`, `Fullscreen`, sem bordas, cobrindo toda a tela incluindo taskbar
  - Ocultar a **taskbar do Windows** (`FindWindow("Shell_TrayWnd")` + `ShowWindow SW_HIDE`)
  - Bloquear **Alt+Tab, Alt+F4, Win, Win+D, Win+R, Ctrl+Esc, Ctrl+Alt+Del, Ctrl+Shift+Esc** via `RegisterHotKey` e `SetWindowsHookEx`
  - Registrar hook de shell para detectar e recolocar a janela ao topo caso perca foco
  - Iniciar `ProcessWatcher` que a cada 2 segundos verifica se algum processo proibido está rodando (taskmgr.exe, regedit.exe, cmd.exe, powershell.exe, explorer.exe fora do shell controlado) e os encerra imediatamente
  - Registrar-se no startup do Windows para iniciar automaticamente com o sistema

### 2. AUTENTICAÇÃO

**Tela de Login do Usuário:**
- Campo: nome de usuário ou PIN numérico
- Exibir foto do usuário cadastrado (opcional)
- Verificar se está dentro do horário/tempo permitido para aquele usuário
- Se fora do horário, exibir mensagem amigável com o próximo horário disponível

**Login do Administrador:**
- Botão discreto "Admin" na tela de login (ícone de engrenagem no rodapé)
- Solicitar senha admin (hash bcrypt armazenado no SQLite)
- Senha padrão inicial: `APAC@Admin2024` (exibir alerta para trocar no primeiro acesso)
- Abrir o **Painel de Administração** em modo seguro (sem expor o desktop)

**Stub Biométrico:**
- Criar interface `IBiometricProvider` com métodos `Enroll(userId)` e `Verify() → userId`
- Implementação padrão retorna `NotImplementedException` com mensagem "Módulo biométrico não instalado"
- Documentar como integrar futuramente com SDK de leitores digitais (ex: Nitgen, Suprema)

### 3. PAINEL DE ADMINISTRAÇÃO

O painel admin deve ter navegação lateral com as seguintes seções:

#### 3.1 Dashboard
- Usuários ativos agora
- Tempo total de uso hoje
- Últimas atividades registradas (tabela com usuário, hora, site acessado)
- Alertas recentes (processos bloqueados, tentativas de burlar o sistema)

#### 3.2 Gerenciar Usuários
- Listagem de usuários cadastrados com foto (se houver), nome, status (ativo/inativo)
- Botão **Novo Usuário**: campos nome completo, username, PIN numérico (4-8 dígitos), foto (opcional, via câmera ou arquivo), status ativo/inativo
- Botão **Editar** e **Excluir** por usuário
- Por usuário: atribuir **perfil de acesso** (conjunto de regras)

#### 3.3 Perfis de Acesso
- Criar e editar perfis com nome (ex: "Usuário Padrão", "Estudo", "Atendimento")
- Por perfil definir:
  - **Sites permitidos** (lista editável de URLs/domínios — ver seção 3.4)
  - **Regra de tempo:** duração máxima de sessão em minutos (0 = ilimitado)
  - **Regra de horário:** intervalos de horário permitidos (ex: 08:00–11:00, 14:00–17:00), com seleção por dia da semana
  - **Pausa obrigatória:** após X minutos de uso, forçar pausa de Y minutos

#### 3.4 Sites Permitidos
- Lista global de sites + listas por perfil
- Interface para adicionar URL ou domínio (ex: `wikipedia.org`, `https://enem.inep.gov.br`)
- Suporte a wildcard simples: `*.gov.br` permite todos os subdomínios de gov.br
- Opção de **testar URL** antes de salvar (verifica se a URL resolve e se seria permitida)
- Botões: Adicionar, Remover, Importar lista (.txt, um domínio por linha), Exportar lista

#### 3.5 Configurações de Monitoramento
Configurar separadamente cada módulo:

**Screenshots:**
- Ativar/desativar
- Intervalo em segundos (padrão: 60s, mínimo: 10s)
- Qualidade da imagem (Alta/Média/Baixa — afeta tamanho do arquivo)
- Pasta de destino (seleção via FolderBrowserDialog)

**Câmera:**
- Ativar/desativar
- Selecionar dispositivo de câmera (dropdown com câmeras detectadas)
- Intervalo em segundos (padrão: 120s, mínimo: 30s)
- Qualidade (Alta/Média/Baixa)
- Pasta de destino (pode ser a mesma dos screenshots ou separada)

**Keylogger:**
- Ativar/desativar
- Pasta de destino para arquivos de log de teclado
- Formato do arquivo: texto plano com timestamp por linha
- Opção de criar novo arquivo por sessão ou por dia

**Geral:**
- Retenção de logs: manter logs por X dias (padrão: 30 dias), excluir automaticamente mais antigos
- Tamanho máximo da pasta de logs em GB (alerta quando atingir 80%)

#### 3.6 Configurações do Sistema
- Trocar senha do administrador (exige senha atual + nova senha + confirmação)
- Nome exibido na tela de login
- Logo personalizado (substituir logo APAC por logo da unidade local, formato PNG/JPG)
- Mensagem de boas-vindas customizável
- Configurar processo de inicialização automática (ativar/desativar autostart)
- **Botão de emergência:** desativar kiosk temporariamente por X minutos (requer senha + justificativa logada)

#### 3.7 Visualizador de Logs
- Filtros: usuário, data/hora início–fim, tipo (screenshot / câmera / teclado / eventos do sistema)
- Galeria de screenshots com miniatura + timestamp + usuário
- Galeria de fotos da câmera
- Log de teclado: exibir como texto com timestamps, filtrar por sessão/usuário
- Log de eventos do sistema (processos bloqueados, tentativas de fechar, logins, logouts)
- Botão exportar logs filtrados (ZIP com imagens + TXT)

### 4. BROWSER KIOSK (WebView2)

- Abrir em tela cheia sem barra de endereço nativa
- Barra de navegação própria minimalista: botão Voltar, botão Avançar, campo de URL (somente leitura, exibe URL atual), botão Início (volta ao site padrão configurado), relógio com tempo restante da sessão
- Interceptar `NavigationStarting` event: antes de cada navegação, verificar se o domínio/URL está na lista de permitidos do perfil ativo
  - Se **permitido:** prosseguir normalmente
  - Se **bloqueado:** cancelar navegação, exibir página interna de bloqueio com mensagem: "Este site não está disponível. Entre em contato com o administrador." e logo APAC
- Bloquear download de arquivos (cancelar via `DownloadStarting` event) exceto extensões configuradas como permitidas
- Bloquear abertura de novas janelas (`NewWindowRequested` event — abrir na mesma janela)
- Desabilitar menu de contexto do botão direito
- Desabilitar DevTools (F12)

### 5. MONITORAMENTO EM TEMPO REAL

Todos os módulos de monitoramento devem rodar em **threads separadas** e não bloquear a UI.

**ScreenCapture.cs:**
```csharp
// A cada N segundos (configurável):
// 1. Capturar a tela com Graphics.CopyFromScreen
// 2. Redimensionar conforme qualidade configurada
// 3. Salvar como JPEG com nome: screenshot_YYYYMMDD_HHMMSS_[usuario].jpg
// 4. Registrar no banco: tabela log_entries (tipo, caminho_arquivo, usuario_id, timestamp)
```

**CameraCapture.cs:**
```csharp
// A cada N segundos (configurável):
// 1. Inicializar câmera selecionada via AForge.Video.DirectShow
// 2. Capturar frame
// 3. Salvar como JPEG: camera_YYYYMMDD_HHMMSS_[usuario].jpg
// 4. Registrar no banco
// Se câmera não disponível: registrar evento "câmera não encontrada" e continuar sem erro
```

**KeyLogger.cs:**
```csharp
// Via SetWindowsHookEx(WH_KEYBOARD_LL):
// 1. Capturar cada tecla pressionada com timestamp
// 2. Incluir teclas especiais: [ENTER], [BACKSPACE], [TAB], [CTRL+C], etc.
// 3. Gravar em arquivo texto: keylog_YYYYMMDD_[usuario].txt
// Formato: [2024-03-15 14:32:10] usuario1: h
//          [2024-03-15 14:32:10] usuario1: e
//          [2024-03-15 14:32:11] usuario1: l
```

### 6. SEGURANÇA EXTREMA

Implementar todas as seguintes camadas de proteção:

```
CAMADA 1 — Bloqueio de Hotkeys
- RegisterHotKey para capturar e suprimir: Win, Win+D, Win+R, Win+L, Win+E, 
  Alt+F4, Alt+Tab, Alt+Esc, Ctrl+Esc, Ctrl+Alt+Del (via SAS hook), 
  Ctrl+Shift+Esc, F11

CAMADA 2 — Watchdog de Processo
- Thread separada, loop a cada 2 segundos
- Lista de processos proibidos: taskmgr, regedit, cmd, powershell, 
  powershell_ise, mmc, msconfig, gpedit, eventvwr, procexp, procmon,
  wireshark, fiddler, x64dbg, ollydbg, autoruns
- Para cada processo encontrado: Process.Kill() imediatamente
- Registrar tentativa no log de eventos

CAMADA 3 — Janela Sempre ao Topo
- SetWindowPos(HWND_TOPMOST) a cada 1 segundo via timer
- Hook de evento de shell (SetWinEventHook EVENT_SYSTEM_FOREGROUND)
- Se outra janela ganhar foco: BringWindowToFront() e SetFocus() imediatamente

CAMADA 4 — Proteção de Processo Próprio
- Abrir handle do próprio processo com PROCESS_ALL_ACCESS
- SetProcessWorkingSetSize para evitar swap
- Opcional: SetCriticalProcess (cuidado — causa BSOD se o processo morrer; 
  usar apenas em produção após testes exaustivos)

CAMADA 5 — Proteção do Registro
- Verificar periodicamente se a chave de autostart ainda existe
- Se removida: recriar automaticamente

CAMADA 6 — Senha para Fechar
- Interceptar o evento de fechamento da janela (FormClosing)
- Exibir diálogo modal de confirmação solicitando senha admin
- Sem senha correta: cancelar o fechamento (e.Cancel = true)
- Com senha correta: restaurar taskbar, desregistrar hooks, fechar ordenadamente
```

### 7. BANCO DE DADOS (SQLite)

```sql
-- Tabelas necessárias:

CREATE TABLE admins (
    id INTEGER PRIMARY KEY,
    username TEXT NOT NULL,
    password_hash TEXT NOT NULL, -- bcrypt
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE users (
    id INTEGER PRIMARY KEY,
    full_name TEXT NOT NULL,
    username TEXT UNIQUE NOT NULL,
    pin_hash TEXT NOT NULL,
    photo_path TEXT,
    profile_id INTEGER REFERENCES access_profiles(id),
    is_active INTEGER DEFAULT 1,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE access_profiles (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    max_session_minutes INTEGER DEFAULT 0,
    mandatory_break_after_minutes INTEGER DEFAULT 0,
    mandatory_break_duration_minutes INTEGER DEFAULT 0
);

CREATE TABLE schedule_rules (
    id INTEGER PRIMARY KEY,
    profile_id INTEGER REFERENCES access_profiles(id),
    day_of_week INTEGER, -- 0=Dom, 1=Seg, ..., 6=Sab; NULL = todos os dias
    start_time TEXT, -- HH:MM
    end_time TEXT    -- HH:MM
);

CREATE TABLE allowed_sites (
    id INTEGER PRIMARY KEY,
    profile_id INTEGER REFERENCES access_profiles(id), -- NULL = global
    url_pattern TEXT NOT NULL, -- domínio ou padrão com wildcard
    added_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE sessions (
    id INTEGER PRIMARY KEY,
    user_id INTEGER REFERENCES users(id),
    login_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    logout_at DATETIME,
    duration_seconds INTEGER
);

CREATE TABLE log_entries (
    id INTEGER PRIMARY KEY,
    session_id INTEGER REFERENCES sessions(id),
    user_id INTEGER REFERENCES users(id),
    type TEXT NOT NULL, -- 'screenshot', 'camera', 'keylog', 'event', 'blocked_url', 'blocked_process'
    file_path TEXT,
    details TEXT,
    timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE system_config (
    key TEXT PRIMARY KEY,
    value TEXT
);
-- Chaves: screenshot_enabled, screenshot_interval_sec, screenshot_quality,
--         screenshot_folder, camera_enabled, camera_interval_sec, camera_device_id,
--         camera_folder, keylog_enabled, keylog_folder, log_retention_days,
--         welcome_message, kiosk_name, logo_path, default_site
```

### 8. TELA DE LOGIN / SPLASH

**SplashScreen:**
- Fundo azul escuro (#1a237e) ou fundo branco com elementos da APAC
- Logo APAC centralizado (carregar de `Assets/logo_apac.png`; se não existir, usar placeholder textual)
- Nome: "APAC Kiosk Guardian" em fonte grande
- Versão e nome do desenvolvedor: "v1.0 — Desenvolvido por Dionathan Iannini"
- Animação de carregamento enquanto o sistema inicializa e aplica as proteções
- Transição suave para a tela de login após inicialização completa

**LoginScreen:**
- Exibir nome da unidade APAC configurado (padrão: "APAC")
- Relógio digital em tempo real
- Campo de usuário + PIN
- Mensagem de boas-vindas configurável
- Ícone de engrenagem discreto para acesso admin

### 9. INSTALADOR

Criar instalador com Inno Setup que:
- Instala o programa em `C:\Program Files\APACKioskGuardian\`
- Copia `Assets\logo_apac.png` para a pasta de instalação
- Registra no autostart do Windows (`HKLM\...\Run`)
- Cria atalho na área de trabalho
- Cria pasta de logs padrão em `C:\APACKiosk\Logs\`
- Cria o banco SQLite inicial com admin padrão e configurações padrão
- Exibe aviso sobre desativar o Windows Defender para o executável (opcional)
- Suporta desinstalação limpa (remove registro, mas mantém logs)

---

## ORDEM DE IMPLEMENTAÇÃO SUGERIDA

1. **Fase 1 — Fundação de Segurança**
   - Projeto base WinForms/WPF
   - Implementar `SecurityManager`, `ProcessWatcher`, `HotkeyBlocker`
   - Testar que a janela não pode ser fechada ou minimizada

2. **Fase 2 — Auth e Banco**
   - SQLite + migrations iniciais
   - Tela de Login, autenticação admin, senha padrão com aviso de troca
   - Sessões

3. **Fase 3 — Browser Kiosk**
   - WebView2 em tela cheia
   - Filtro de URLs
   - Barra de navegação própria

4. **Fase 4 — Monitoramento**
   - ScreenCapture
   - CameraCapture
   - KeyLogger

5. **Fase 5 — Painel Admin**
   - Dashboard, Usuários, Perfis, Sites, Config Monitoramento
   - Visualizador de Logs

6. **Fase 6 — Polimento**
   - SplashScreen com logo APAC
   - Instalador Inno Setup
   - Testes de segurança (tentativas de fechar, Task Manager, etc.)
   - Stub biométrico documentado

---

## OBSERVAÇÕES IMPORTANTES PARA O AGENTE

1. **Não usar bibliotecas pagas** — toda a stack deve ser open source ou nativa do Windows/.NET
2. **Compatibilidade:** Windows 10 e Windows 11 (x64)
3. **Executar como Administrador** — o manifesto da aplicação deve exigir `requireAdministrator`
4. **Tratamento de erros robusto** — qualquer exceção não tratada não deve fechar o kiosk; logar e continuar
5. **Sem dependência de internet** para funcionar — o banco e configurações são locais
6. **Internacionalização:** textos em português brasileiro (pt-BR)
7. **Logo APAC:** carregar de arquivo externo `Assets/logo_apac.png` para facilitar customização por unidade
8. **Documentar** com XML comments os métodos principais para facilitar manutenção futura

---

*Prompt elaborado para o projeto APAC Kiosk Guardian — Dionathan Iannini*  
*Uso exclusivo nas unidades APAC do Brasil*
