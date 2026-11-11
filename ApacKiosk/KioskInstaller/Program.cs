using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.KioskInstaller;

static class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        var command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "--install":
            case "install":
                Install(args);
                break;
            case "--uninstall":
            case "uninstall":
                Uninstall(args);
                break;
            case "--configure":
            case "configure":
                Configure(args);
                break;
            case "--help":
            case "help":
                PrintUsage();
                break;
            default:
                Console.WriteLine($"Comando desconhecido: {command}");
                PrintUsage();
                break;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("APAC Kiosk Guardian — Instalador");
        Console.WriteLine();
        Console.WriteLine("Uso:");
        Console.WriteLine("  KioskInstaller install   --user <kiosk_user> [--admin <admin_user>]");
        Console.WriteLine("  KioskInstaller uninstall --user <kiosk_user>");
        Console.WriteLine("  KioskInstaller configure --user <kiosk_user> [--admin <admin_user>]");
        Console.WriteLine("  KioskInstaller help");
    }

    private static void Install(string[] args)
    {
        var kioskUser = GetArgValue(args, "--user") ?? "kiosk_apac";
        var adminUser = GetArgValue(args, "--admin") ?? "admin_apac";

        Console.WriteLine("=== APAC Kiosk Guardian — Instalacao ===");
        Console.WriteLine($"Usuario Kiosk: {kioskUser}");
        Console.WriteLine($"Usuario Admin: {adminUser}");
        Console.WriteLine();

        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var installDir = Path.GetDirectoryName(exePath) ?? "";
        var kioskExePath = Path.Combine(installDir, "ApacKiosk.exe");

        EnsureDataDirectory();

        InstallWindowsService(kioskExePath);
        SetupKioskUserShell(kioskUser, kioskExePath);
        SetupTaskManagerRestriction(kioskUser, true);
        ConfigureKioskSettings(kioskExePath);

        Console.WriteLine();
        Console.WriteLine("Instalacao concluida com sucesso.");
        Console.WriteLine();
        Console.WriteLine("Proximos passos:");
        Console.WriteLine("1. Execute como administrador:");
        Console.WriteLine($"   net user {kioskUser} SenhaSegura123! /add");
        Console.WriteLine($"   net localgroup Users {kioskUser} /add");
        Console.WriteLine($"2. Execute: KioskInstaller configure --user {kioskUser} --admin {adminUser}");
        Console.WriteLine("3. Reinicie o computador ou faca login na conta kiosk");
    }

    private static void Uninstall(string[] args)
    {
        var kioskUser = GetArgValue(args, "--user") ?? "kiosk_apac";

        Console.WriteLine("=== APAC Kiosk Guardian — Desinstalacao ===");
        Console.WriteLine();

        UninstallWindowsService();
        SetupKioskUserShell(kioskUser, null);
        SetupTaskManagerRestriction(kioskUser, false);

        Console.WriteLine("Desinstalacao concluida. Reinicie o computador.");
    }

    private static void Configure(string[] args)
    {
        var kioskUser = GetArgValue(args, "--user") ?? "kiosk_apac";
        var adminUser = GetArgValue(args, "--admin") ?? "admin_apac";

        Console.WriteLine("=== APAC Kiosk Guardian — Configuracao ===");
        Console.WriteLine();

        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var installDir = Path.GetDirectoryName(exePath) ?? "";
        var kioskExePath = Path.Combine(installDir, "ApacKiosk.exe");

        SetupKioskUserShell(kioskUser, kioskExePath);
        SetupTaskManagerRestriction(kioskUser, true);

        Console.WriteLine("Configuracao concluida.");
    }

    private static void EnsureDataDirectory()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ApacKiosk");
        Directory.CreateDirectory(appData);

        var dbPath = Path.Combine(appData, "apac.db");
        if (!File.Exists(dbPath))
        {
            var db = new DatabaseManager(dbPath);
            db.Initialize();
        }
    }

    private static void InstallWindowsService(string kioskExePath)
    {
        try
        {
            using var sc = new ServiceController("KioskGuardianService");
            if (sc.Status == ServiceControllerStatus.Running)
                sc.Stop();
        }
        catch { }

        try
        {
            var serviceExePath = Path.Combine(
                Path.GetDirectoryName(kioskExePath) ?? "",
                "KioskService.exe");

            if (File.Exists(serviceExePath))
            {
                var psi = new ProcessStartInfo("sc.exe",
                    $"create KioskGuardianService binPath= \"{serviceExePath}\" start= auto DisplayName= \"APAC Kiosk Guardian Service\"")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();

                psi = new ProcessStartInfo("sc.exe", "start KioskGuardianService")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                proc = Process.Start(psi);
                proc?.WaitForExit();

                Console.WriteLine("KioskService registrado como Windows Service.");
            }
            else
            {
                Console.WriteLine($"AVISO: KioskService.exe nao encontrado em {serviceExePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao instalar o service: {ex.Message}");
        }
    }

    private static void UninstallWindowsService()
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", "stop KioskGuardianService")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit();

            psi = new ProcessStartInfo("sc.exe", "delete KioskGuardianService")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            proc = Process.Start(psi);
            proc?.WaitForExit();

            Console.WriteLine("KioskService removido do Windows Service.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao remover o service: {ex.Message}");
        }
    }

    private static void SetupKioskUserShell(string kioskUser, string? kioskExePath)
    {
        try
        {
            var kioskSid = GetUserSid(kioskUser);
            if (kioskSid == null)
            {
                Console.WriteLine($"Usuario {kioskUser} nao encontrado. Execute net user {kioskUser} /add primeiro.");
                return;
            }

            var winlogonPath = $@"{kioskSid}\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

            if (kioskExePath != null)
            {
                using var key = Registry.Users.OpenSubKey(winlogonPath, writable: true)
                    ?? Registry.Users.CreateSubKey(winlogonPath);
                key.SetValue("Shell", kioskExePath, RegistryValueKind.String);
                Console.WriteLine($"Shell configurado para {kioskUser}: {kioskExePath}");
            }
            else
            {
                using var key = Registry.Users.OpenSubKey(winlogonPath, writable: true);
                if (key != null)
                {
                    key.DeleteValue("Shell", throwOnMissingValue: false);
                    Console.WriteLine($"Shell removido para {kioskUser} (restaurado padrao).");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao configurar shell: {ex.Message}");
            Console.WriteLine("Tente carregar o hive do usuario primeiro:");
            Console.WriteLine($"  reg load HKU\\{kioskUser} C:\\Users\\{kioskUser}\\NTUSER.DAT");
        }
    }

    private static void SetupTaskManagerRestriction(string kioskUser, bool block)
    {
        try
        {
            var kioskSid = GetUserSid(kioskUser);
            if (kioskSid == null) return;

            var policiesPath = $@"{kioskSid}\Software\Microsoft\Windows\CurrentVersion\Policies\System";
            var explorerPath = $@"{kioskSid}\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

            if (block)
            {
                using var sysKey = Registry.Users.OpenSubKey(policiesPath, writable: true)
                    ?? Registry.Users.CreateSubKey(policiesPath);
                sysKey.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);

                using var expKey = Registry.Users.OpenSubKey(explorerPath, writable: true)
                    ?? Registry.Users.CreateSubKey(explorerPath);
                expKey.SetValue("NoClose", 1, RegistryValueKind.DWord);

                Console.WriteLine($"Task Manager bloqueado para {kioskUser}");
            }
            else
            {
                using var sysKey = Registry.Users.OpenSubKey(policiesPath, writable: true);
                sysKey?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);

                using var expKey = Registry.Users.OpenSubKey(explorerPath, writable: true);
                expKey?.DeleteValue("NoClose", throwOnMissingValue: false);

                Console.WriteLine($"Task Manager liberado para {kioskUser}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao configurar Task Manager: {ex.Message}");
        }
    }

    private static void ConfigureKioskSettings(string kioskExePath)
    {
        try
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ApacKiosk");
            var dbPath = Path.Combine(appData, "apac.db");
            var db = new DatabaseManager(dbPath);
            db.Initialize();
            var config = new ConfigManager(db);

            config.MonitorIntervalSec = 3;
            config.KioskExePath = kioskExePath;

            Console.WriteLine("Configuracoes do kiosk atualizadas.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao configurar settings: {ex.Message}");
        }
    }

    private static string? GetUserSid(string username)
    {
        try
        {
            var ntAccount = new System.Security.Principal.NTAccount(username);
            var sid = (System.Security.Principal.SecurityIdentifier)ntAccount.Translate(typeof(System.Security.Principal.SecurityIdentifier));
            return sid.Value;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
