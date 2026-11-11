using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ApacKiosk.Utils;

public static class UninstallHelper
{
    public const string TargetDir = @"C:\Program Files\APAC Kiosk Guardian";
    public const string TargetExe = @"C:\Program Files\APAC Kiosk Guardian\ApacKiosk.exe";
    public const string ProgramDataDir = @"C:\ProgramData\ApacKiosk";
    private const string UninstallRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\APAC Kiosk Guardian";

    public static void RegisterUninstall()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.5.0";

            void WriteKey(RegistryKey? baseKey)
            {
                if (baseKey == null) return;
                using var key = baseKey.CreateSubKey(UninstallRegKey);
                if (key != null)
                {
                    key.SetValue("DisplayName", "APAC Kiosk Guardian");
                    key.SetValue("DisplayVersion", version);
                    key.SetValue("Publisher", "APAC");
                    key.SetValue("InstallLocation", TargetDir);
                    key.SetValue("DisplayIcon", $"{TargetExe},0");
                    key.SetValue("UninstallString", $"\"{TargetExe}\" --uninstall");
                    key.SetValue("QuietUninstallString", $"\"{TargetExe}\" --uninstall --quiet");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("EstimatedSize", 110000, RegistryValueKind.DWord);
                }
            }

            try { WriteKey(Registry.LocalMachine); } catch { }
            try { WriteKey(Registry.CurrentUser); } catch { }
        }
        catch { }
    }

    public static void UnregisterUninstall()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(UninstallRegKey, false);
        }
        catch { }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegKey, false);
        }
        catch { }
    }

    public static void RemoveAutostart()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue("APAC Kiosk Guardian", false);
            key?.DeleteValue("ApacKioskGuardian", false);
        }
        catch { }

        try
        {
            using var cuKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            cuKey?.DeleteValue("APAC Kiosk Guardian", false);
            cuKey?.DeleteValue("ApacKioskGuardian", false);
        }
        catch { }
    }

    public static void RemoveShortcuts()
    {
        var locations = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };

        foreach (var loc in locations)
        {
            if (string.IsNullOrEmpty(loc) || !Directory.Exists(loc)) continue;
            var lnk = Path.Combine(loc, "APAC Kiosk Guardian.lnk");
            if (File.Exists(lnk))
            {
                try { File.Delete(lnk); } catch { }
            }
        }
    }

    public static void ExecuteFullUninstall(bool purgeData)
    {
        int myPid = Process.GetCurrentProcess().Id;
        var procs = Process.GetProcessesByName("ApacKiosk");
        foreach (var p in procs)
        {
            if (p.Id != myPid)
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        RemoveAutostart();
        RemoveShortcuts();
        UnregisterUninstall();

        if (purgeData && Directory.Exists(ProgramDataDir))
        {
            try
            {
                Directory.Delete(ProgramDataDir, true);
            }
            catch { }
        }

        if (Directory.Exists(TargetDir))
        {
            try
            {
                var files = Directory.GetFiles(TargetDir, "*.*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
                Directory.Delete(TargetDir, true);
            }
            catch { }
        }
    }
}
