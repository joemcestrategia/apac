using System;
using System.IO;
using ApacKiosk.Database;
using Microsoft.Win32;

namespace ApacKiosk.Utils;

public static class AutostartManager
{
    public const string OfficialValueName = "APAC Kiosk Guardian";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static readonly string[] AllValueNames =
    {
        "APAC Kiosk Guardian",
        "ApacKioskGuardian",
        "APAC_Kiosk"
    };

    public static void ConfigureAutostart(bool enable, string exePath)
    {
        if (!enable || string.IsNullOrWhiteSpace(exePath))
        {
            RemoveAllAutostart();
            return;
        }

        string normalizedExe = NormalizeExePath(exePath);
        string quotedCommand = $"\"{normalizedExe}\"";
        bool wroteHklm = false;

        try
        {
            using var hklmKey = Registry.LocalMachine.OpenSubKey(RunKeyPath, writable: true);
            if (hklmKey != null)
            {
                if (!string.Equals(hklmKey.GetValue(OfficialValueName)?.ToString(), quotedCommand, StringComparison.OrdinalIgnoreCase))
                    hklmKey.SetValue(OfficialValueName, quotedCommand, RegistryValueKind.String);

                CleanupNonOfficialValues(hklmKey, preserveOfficial: true);
                wroteHklm = true;
            }
        }
        catch { }

        if (wroteHklm)
        {
            CleanupAllValues(Registry.CurrentUser, preserveOfficial: false);
            return;
        }

        bool hasValidHklmEntry = HasValidOfficialEntry(Registry.LocalMachine, normalizedExe);
        if (hasValidHklmEntry)
        {
            CleanupAllValues(Registry.CurrentUser, preserveOfficial: false);
            return;
        }

        try
        {
            using var hkcuKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (hkcuKey != null)
            {
                if (!string.Equals(hkcuKey.GetValue(OfficialValueName)?.ToString(), quotedCommand, StringComparison.OrdinalIgnoreCase))
                    hkcuKey.SetValue(OfficialValueName, quotedCommand, RegistryValueKind.String);

                CleanupNonOfficialValues(hkcuKey, preserveOfficial: true);
            }
        }
        catch { }
    }

    public static void RemoveAllAutostart()
    {
        CleanupAllValues(Registry.LocalMachine, preserveOfficial: false);
        CleanupAllValues(Registry.CurrentUser, preserveOfficial: false);
    }

    public static void EnsureProtectedAutostart(DatabaseManager db, string exePath)
    {
        bool enabled = true;
        try
        {
            var setting = db.GetSetting("autostart_enabled");
            if (!string.IsNullOrWhiteSpace(setting) && bool.TryParse(setting, out bool parsed))
                enabled = parsed;
        }
        catch { }

        if (!enabled)
            return;

        string normalizedExe = NormalizeExePath(exePath);
        if (string.IsNullOrEmpty(normalizedExe))
            return;

        bool hklmValid = HasValidOfficialEntry(Registry.LocalMachine, normalizedExe);
        bool hkcuValid = HasValidOfficialEntry(Registry.CurrentUser, normalizedExe);
        bool hasDuplicates = HasDuplicateOrLegacyEntries(Registry.LocalMachine, hklmValid) ||
                             HasDuplicateOrLegacyEntries(Registry.CurrentUser, !hklmValid && hkcuValid);

        if ((!hklmValid && !hkcuValid) || hasDuplicates)
        {
            ConfigureAutostart(true, normalizedExe);
        }
    }

    private static bool HasValidOfficialEntry(RegistryKey root, string expectedNormalizedExe)
    {
        try
        {
            using var key = root.OpenSubKey(RunKeyPath, writable: false);
            var raw = key?.GetValue(OfficialValueName)?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string unquoted = raw.Trim().Trim('"');
            return string.Equals(NormalizeExePath(unquoted), expectedNormalizedExe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasDuplicateOrLegacyEntries(RegistryKey root, bool expectOfficialHere)
    {
        try
        {
            using var key = root.OpenSubKey(RunKeyPath, writable: false);
            if (key == null) return false;

            foreach (var name in AllValueNames)
            {
                bool isOfficial = string.Equals(name, OfficialValueName, StringComparison.OrdinalIgnoreCase);
                if (isOfficial && expectOfficialHere) continue;
                if (key.GetValue(name) != null) return true;
            }
        }
        catch { }
        return false;
    }

    private static void CleanupAllValues(RegistryKey root, bool preserveOfficial)
    {
        try
        {
            using var key = root.OpenSubKey(RunKeyPath, writable: true);
            if (key != null)
                CleanupNonOfficialValues(key, preserveOfficial);
        }
        catch { }
    }

    private static void CleanupNonOfficialValues(RegistryKey key, bool preserveOfficial)
    {
        foreach (var name in AllValueNames)
        {
            if (preserveOfficial && string.Equals(name, OfficialValueName, StringComparison.OrdinalIgnoreCase))
                continue;
            try { key.DeleteValue(name, throwOnMissingValue: false); } catch { }
        }
    }

    private static string NormalizeExePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return path.Trim().Trim('"');
        }
    }
}
