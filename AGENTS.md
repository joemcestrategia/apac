# AGENTS.md — APAC Kiosk Guardian

## Project Overview

**APAC Kiosk Guardian** is a secure Windows kiosk application for APAC (Associação de Proteção e Assistência aos Condenados) units across Brazil. It locks down a Windows PC, only allowing access to admin-authorized websites and programs, with full monitoring (screenshots, camera, keylogging), user management, and session time controls.

- **Language:** C# (.NET 8.0)
- **UI:** Windows Forms
- **Browser:** Microsoft WebView2
- **Database:** SQLite via `Microsoft.Data.Sqlite`
- **Platform:** Windows 10/11 x64, runs as Administrator
- **UI Language:** pt-BR (Portuguese/Brazil)
- **Entry point:** `ApacKiosk/Program.cs`

## Build / Test / Lint Commands

```bash
# Restore dependencies
dotnet restore ApacKiosk/ApacKiosk.csproj

# Build (Debug)
dotnet build ApacKiosk/ApacKiosk.csproj

# Build (Release)
dotnet build ApacKiosk/ApacKiosk.csproj --configuration Release

# Publish self-contained single-file exe (win-x64)
dotnet publish ApacKiosk/ApacKiosk.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output ./publish
```

**Note:** This project does not have unit tests or a linter. When adding new code, verify the build succeeds: `dotnet build ApacKiosk/ApacKiosk.csproj --configuration Release`.

## Code Conventions

### Namespace & File Organization

```
ApacKiosk/
├── Browser/          # WebView2 browser control
├── Database/         # SQLite data access (DatabaseManager.cs)
├── Forms/            # All WinForms UI
│   └── Admin/        # Admin panel tab pages
├── Interop/          # Win32 P/Invoke (NativeMethods.cs, Win32.cs)
├── Models/           # Data models (Models.cs)
├── Resources/        # Embedded resources (HTML, images)
├── Services/         # Business logic services
└── Utils/            # Utilities (ConfigManager, PasswordHash)
```

### Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Namespaces | PascalCase, file-scoped | `namespace ApacKiosk.Services;` |
| Classes | PascalCase | `SecurityManager`, `LoginForm` |
| Methods | PascalCase | `EnableAllSecurity()`, `GetConnection()` |
| Private fields | `_camelCase` prefix | `_db`, `_isRunning` |
| Public properties | PascalCase | `DisplayName`, `IsAdmin` |
| Local variables | camelCase | `dbPath`, `conn` |
| Constants | PascalCase | `ProhibitedProcesses` |

### Code Style

- **File-scoped namespaces** (C# 10+): `namespace ApacKiosk.Services;`
- **Implicit usings** enabled, nullable reference types enabled
- **No XML doc comments** on existing code (keep this style — only add comments if explicitly requested)
- **Expression-bodied members** for simple one-liners: `public AuthService(DatabaseManager db) => _db = db;`
- **Pattern matching** for null checks: `reader.IsDBNull(4) ? null : reader.GetString(4)`
- **BCrypt** via `BCrypt.Net.BCrypt` for password hashing
- **Database connections** always wrapped in `using var conn = _db.GetConnection();`
- **SQL parameters** use `@paramName` syntax with `cmd.Parameters.AddWithValue("@paramName", value)`
- **DBNull handling:** Cast nullable values as `(object?)value ?? DBNull.Value` or `value.HasValue ? (object)value.Value : DBNull.Value`

### UI Patterns

- All forms inherit from `Form` directly
- UI controls created programmatically in `InitializeComponent()`, NOT via the designer
- Colors using `Color.FromArgb(r, g, b)` — dark theme: background `(10, 10, 25)`, foreground `Color.White`
- Font: `"Segoe UI"` family with various sizes
- Custom dialog overlays via `KioskDialog` class (in-form panels, not native MessageBox)
- Forms are `TopMost = true`, `FormBorderStyle = FormBorderStyle.None`, `WindowState = FormWindowState.Maximized`

### Service Patterns

- Services receive `DatabaseManager` via constructor injection
- Services are instantiated directly in `Program.Main()` and stored as static properties
- Database calls always use `using var conn = _db.GetConnection()` pattern
- Thread-sensitive services (SecurityManager, MonitorService) implement `IDisposable` and use `volatile bool _isRunning` flags

### Interop Patterns

- Win32 functions declared as `private static extern` in `Interop/NativeMethods.cs` or `Interop/Win32.cs`
- Use `[DllImport("user32.dll")]` with `SetLastError = true`
- P/Invoke types: `IntPtr` for handles, `uint` for flags, `[MarshalAs(UnmanagedType.LPWStr)]` for strings

## Architecture & Flow

1. **`Program.Main()`** initializes database, config, all services, and security. Shows splash screen, then enters a login loop.
2. **`LoginForm`** — Toggle between user mode (username + PIN) and admin mode (username + password).
3. **User path:** `KioskDesktopForm` (desktop with shortcut cards) → `KioskBrowserForm` (embedded WebView2 browser with URL filtering).
4. **Admin path:** `AdminDashboard` with 8 tab pages (Dashboard, UserManagement, AccessProfiles, AllowedSites, Programs, MonitoringConfig, SystemConfig, LogViewer).
5. **Security:** Process watchdog (kills prohibited processes), hotkey blocking (Win, Alt+F4, Alt+Tab, Ctrl+Esc), window always-on-top enforcement, registry auto-start protection, password-gated shutdown.
6. **Monitoring:** Background threads for periodic screenshots (`ScreenCapture`), camera captures (`CameraCapture`), and keyboard hook logging (`KeyLogger`), orchestrated by `MonitorService`.

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Data.Sqlite` | 8.0.0 | SQLite database |
| `Microsoft.Web.WebView2` | 1.0.2903.40 | Chromium browser |
| `BCrypt.Net-Next` | 4.0.3 | Password hashing |
| `AForge.Video` | 2.2.5 | Camera capture |
| `AForge.Video.DirectShow` | 2.2.5 | Camera device detection |
| `System.Drawing.Common` | 8.0.0 | Image/GDI+ |

**Do NOT add new NuGet packages without explicit approval.** If proposing a new dependency, explain why it's needed and confirm it is free/open-source.

## When Making Changes

1. **Build first** — always run `dotnet build ApacKiosk/ApacKiosk.csproj --configuration Release` after changes
2. **Match existing patterns** — look at similar files in the same directory for conventions
3. **No comments** — do not add comments unless explicitly asked
4. **Portuguese text** — all user-facing strings must be in pt-BR
5. **Always-on-top** — new forms must use `TopMost = true` and `FormBorderStyle = FormBorderStyle.None`
6. **Error resilience** — the kiosk must never crash to desktop; log errors to trace/crash log files
7. **Thread safety** — any long-running operations must run on background threads, not the UI thread
8. **Database migrations** — if adding new columns/tables, add them to `DatabaseManager.Initialize()` using `CREATE TABLE IF NOT EXISTS` and `ALTER TABLE ... ADD COLUMN` wrapped in try/catch

## Common Tasks Reference

### Adding a new config setting
1. Add a property in `Utils/ConfigManager.cs` following the existing pattern
2. Update relevant forms/services that consume the setting

### Adding a new admin tab
1. Create the tab class in `Forms/Admin/` following existing tab patterns
2. Add it to `AdminDashboard.InitializeTabs()`

### Adding a new monitoring module
1. Create the service class in `Services/` with constructor `(ConfigManager config, DatabaseManager db)`
2. Add it to `MonitorService` for orchestration
3. Add config properties to `ConfigManager`
4. Add tab controls in `MonitoringConfigTab`

## OTA, Offline Installer & Zero-UAC Update Rules

1. **Zero-UAC updates:** Updates must never prompt the user for UAC confirmation or expose the desktop/taskbar/Start Menu. Always preserve and use the Windows service `ApacKioskUpdater` (`Services/WindowsUpdaterService.cs`, running `ApacUpdaterHost.exe --update-service` as `LocalSystem`), verifying package SHA-256 against the official GitHub release before applying.
2. **Offline installer with every OTA release:** Always use `scripts/publish_ota.ps1` so every OTA release simultaneously updates `ApacKiosk.Installer/Payload.zip`, builds the self-contained offline installer (`publish/Instalador_ApacKiosk_vX.Y.Z.W.exe`), and attaches it to the GitHub Release alongside `ApacKiosk.exe` and `ApacKiosk.exe.sha256` (without attaching any second `.sha256` file that could conflict with legacy OTA clients). Use `-LocalOnly` when testing an offline build before publishing to GitHub.
3. **Internal logs only:** Write detailed update diagnostics only to system log files (`%TEMP%\ApacKiosk_Update.log`, `C:\ProgramData\ApacKiosk\ApacKiosk_Update.log`, and `%TEMP%\ApacKiosk_UpdateError.log`); never expose technical log boxes or copy-log controls in the UI (`SplashScreen`, `UpdaterStatusForm`, `UpdateStatusDialog`, `SystemConfigTab`).
4. **Anti-regression invariants (see `docs/OTA_E_SEGURANCA_KIOSK.md`):**
   - Always call `Program.ProcessWatcher?.Pause()` and register the updater executable/PID before starting any updater child process, and keep `userinit.exe`, `ApacUpdaterHost.exe`, `ApacHostRefresher.exe`, `ApacKioskUninstall.exe`, and `SessionId == 0` exempted in `ProcessWatcher`.
   - Never exit the parent Kiosk process (`ShutdownPreservingLock()` + `Environment.Exit(0)`) unless `ui_ready_<token>.flag` is confirmed; on any premature exit or timeout, resume `ProcessWatcher` and stay locked in the Kiosk UI.
   - Keep a single autostart entry via `AutostartManager` (`APAC Kiosk Guardian` in `HKLM\...\Run`), removing legacy duplicates (`ApacKioskGuardian`, `APAC_Kiosk`).
   - Apply/remove Windows `Ctrl+Alt+Del` policies through `WindowsUpdaterService` (`SET_CTRL_ALT_DEL_LOCK` using `RunAsClient` SID) so standard users without UAC cannot leave policies stuck and always restore everything in `DisableAllSecurity()`.
