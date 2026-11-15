using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using ApacKiosk.Database;
using Microsoft.Win32;

namespace ApacKiosk.Services;

public class UserFolderEnvironmentService
{
    private readonly DatabaseManager _db;

    private const string SettingActive = "env_redirect_active";
    private const string SettingBackupJson = "env_redirect_backup_json";
    private const string SettingOriginalDocs = "env_orig_documents_path";
    private const string AbsentMarker = "__ABSENT__";

    private static readonly Guid FolderIdDocuments = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private static readonly string[] OfficeVersions = { "16.0", "15.0" };
    private static readonly string[] OfficeApps = { "Word", "Excel", "PowerPoint" };

    public string OriginalDocumentsRoot { get; private set; }
    public string CurrentUserFolder { get; private set; } = string.Empty;
    public bool IsIsolationActive { get; private set; }

    public UserFolderEnvironmentService(DatabaseManager db)
    {
        _db = db;
        OriginalDocumentsRoot = ResolveOriginalDocumentsRoot();
    }

    public string GetUserFolderPath(string? username)
    {
        var baseDocs = ResolveOriginalDocumentsRoot();
        var folderName = SanitizeFolderName(username);
        var userPath = Path.Combine(baseDocs, folderName);
        Directory.CreateDirectory(userPath);
        return NormalizePath(userPath);
    }

    public void ApplyUserIsolation(string? username)
    {
        try
        {
            var userFolder = GetUserFolderPath(username);
            CurrentUserFolder = userFolder;

            if (_db.GetSetting(SettingActive, "false") != "true")
            {
                SaveOriginalBackup();
                _db.SetSetting(SettingActive, "true");
            }

            SetDocumentsKnownFolder(userFolder);
            ApplyOfficeSettings(userFolder);
            ApplyDialogPolicies();
            NotifyEnvironmentChanged();
            IsIsolationActive = true;
        }
        catch (Exception ex)
        {
            LogTrace($"ApplyUserIsolation erro: {ex.Message}");
        }
    }

    public void RestoreOriginalEnvironment()
    {
        try
        {
            var wasActive = _db.GetSetting(SettingActive, "false") == "true";
            var backupJson = _db.GetSetting(SettingBackupJson, "");
            if (!wasActive && string.IsNullOrWhiteSpace(backupJson) && !IsIsolationActive)
                return;

            var origDocs = ResolveOriginalDocumentsRoot();
            if (!string.IsNullOrWhiteSpace(origDocs) && Directory.Exists(origDocs))
                SetDocumentsKnownFolder(origDocs);

            if (!string.IsNullOrWhiteSpace(backupJson))
            {
                var entries = JsonSerializer.Deserialize<List<RegistryBackupEntry>>(backupJson);
                if (entries != null)
                {
                    foreach (var entry in entries)
                        RestoreRegistryEntry(entry);
                }
            }

            _db.SetSetting(SettingActive, "false");
            _db.SetSetting(SettingBackupJson, "");
            CurrentUserFolder = string.Empty;
            IsIsolationActive = false;
            NotifyEnvironmentChanged();
        }
        catch (Exception ex)
        {
            LogTrace($"RestoreOriginalEnvironment erro: {ex.Message}");
        }
    }

    public bool IsDocumentsKnownFolderPointingTo(string expectedPath)
    {
        try
        {
            var current = GetDocumentsKnownFolder();
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(expectedPath))
                return false;
            return string.Equals(NormalizePath(current), NormalizePath(expectedPath), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string ResolveOriginalDocumentsRoot()
    {
        var saved = _db.GetSetting(SettingOriginalDocs, "");
        var isActive = _db.GetSetting(SettingActive, "false") == "true";
        if (isActive && !string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
            return NormalizePath(saved);

        var current = GetDocumentsKnownFolder();
        if (string.IsNullOrWhiteSpace(current))
            current = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        current = NormalizePath(current);

        if (!isActive)
            _db.SetSetting(SettingOriginalDocs, current);
        else if (!string.IsNullOrWhiteSpace(saved))
            return NormalizePath(saved);

        return current;
    }

    private void SaveOriginalBackup()
    {
        var entries = new List<RegistryBackupEntry>();

        AddBackup(entries, @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Personal");
        AddBackup(entries, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", "Personal");
        AddBackup(entries, @"Software\Microsoft\Windows\CurrentVersion\Policies\Comdlg32", "NoPlacesBar");
        AddBackup(entries, @"Software\Microsoft\Windows\CurrentVersion\Policies\Comdlg32", "NoBackButton");
        AddBackup(entries, @"Software\Microsoft\Windows\CurrentVersion\Policies\Comdlg32", "NoFileMru");
        AddBackup(entries, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsHistory");

        foreach (var ver in OfficeVersions)
        {
            if (!SubKeyExists($@"Software\Microsoft\Office\{ver}"))
                continue;

            AddBackup(entries, $@"Software\Microsoft\Office\{ver}\Word\Options", "DOC-PATH");
            AddBackup(entries, $@"Software\Microsoft\Office\{ver}\Excel\Options", "DefaultPath");
            AddBackup(entries, $@"Software\Microsoft\Office\{ver}\PowerPoint\RecentFolderList", "Default");
            AddBackup(entries, $@"Software\Microsoft\Office\{ver}\Common\General", "PreferCloudSaveLocations");
            AddBackup(entries, $@"Software\Microsoft\Office\{ver}\Common\General", "SkipOpenAndSaveAsPlace");
            AddBackup(entries, $@"Software\Policies\Microsoft\Office\{ver}\Common\Internet", "onlinestorage");

            foreach (var app in OfficeApps)
            {
                AddBackup(entries, $@"Software\Microsoft\Office\{ver}\{app}\File MRU", "Max Display");
                AddBackup(entries, $@"Software\Microsoft\Office\{ver}\{app}\Place MRU", "Max Display");
                AddBackup(entries, $@"Software\Policies\Microsoft\Office\{ver}\{app}\File MRU", "Max Display");
                AddBackup(entries, $@"Software\Policies\Microsoft\Office\{ver}\{app}\Place MRU", "Max Display");

                foreach (var mruSubKey in GetUserMruSubKeys(ver, app))
                    AddBackup(entries, mruSubKey, "Max Display");
            }
        }

        _db.SetSetting(SettingBackupJson, JsonSerializer.Serialize(entries));
    }

    private void ApplyOfficeSettings(string userFolder)
    {
        foreach (var ver in OfficeVersions)
        {
            if (!SubKeyExists($@"Software\Microsoft\Office\{ver}"))
                continue;

            SetRegistryValue($@"Software\Microsoft\Office\{ver}\Word\Options", "DOC-PATH", userFolder, RegistryValueKind.ExpandString);
            SetRegistryValue($@"Software\Microsoft\Office\{ver}\Excel\Options", "DefaultPath", userFolder, RegistryValueKind.ExpandString);
            SetRegistryValue($@"Software\Microsoft\Office\{ver}\PowerPoint\RecentFolderList", "Default", userFolder, RegistryValueKind.ExpandString);
            SetRegistryValue($@"Software\Microsoft\Office\{ver}\Common\General", "PreferCloudSaveLocations", 0, RegistryValueKind.DWord);
            SetRegistryValue($@"Software\Microsoft\Office\{ver}\Common\General", "SkipOpenAndSaveAsPlace", 1, RegistryValueKind.DWord);
            SetRegistryValue($@"Software\Policies\Microsoft\Office\{ver}\Common\Internet", "onlinestorage", 211, RegistryValueKind.DWord);

            foreach (var app in OfficeApps)
            {
                SetRegistryValue($@"Software\Microsoft\Office\{ver}\{app}\File MRU", "Max Display", 0, RegistryValueKind.DWord);
                SetRegistryValue($@"Software\Microsoft\Office\{ver}\{app}\Place MRU", "Max Display", 0, RegistryValueKind.DWord);
                SetRegistryValue($@"Software\Policies\Microsoft\Office\{ver}\{app}\File MRU", "Max Display", 0, RegistryValueKind.DWord);
                SetRegistryValue($@"Software\Policies\Microsoft\Office\{ver}\{app}\Place MRU", "Max Display", 0, RegistryValueKind.DWord);

                foreach (var mruSubKey in GetUserMruSubKeys(ver, app))
                    SetRegistryValue(mruSubKey, "Max Display", 0, RegistryValueKind.DWord);
            }
        }
    }

    private static void ApplyDialogPolicies()
    {
        SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Policies\Comdlg32", "NoPlacesBar", 1, RegistryValueKind.DWord);
        SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Policies\Comdlg32", "NoBackButton", 1, RegistryValueKind.DWord);
        SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Policies\Comdlg32", "NoFileMru", 1, RegistryValueKind.DWord);
        SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsHistory", 1, RegistryValueKind.DWord);
    }

    private static List<string> GetUserMruSubKeys(string ver, string app)
    {
        var result = new List<string>();
        try
        {
            var basePath = $@"Software\Microsoft\Office\{ver}\{app}\User MRU";
            using var baseKey = Registry.CurrentUser.OpenSubKey(basePath);
            if (baseKey == null) return result;

            foreach (var profileName in baseKey.GetSubKeyNames())
            {
                var fileMru = $@"{basePath}\{profileName}\File MRU";
                var placeMru = $@"{basePath}\{profileName}\Place MRU";
                if (SubKeyExists(fileMru)) result.Add(fileMru);
                if (SubKeyExists(placeMru)) result.Add(placeMru);
            }
        }
        catch { }
        return result;
    }

    private static void AddBackup(List<RegistryBackupEntry> entries, string subKey, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            if (key == null)
            {
                entries.Add(new RegistryBackupEntry { SubKey = subKey, ValueName = valueName, RawValue = AbsentMarker });
                return;
            }

            var val = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (val == null)
            {
                entries.Add(new RegistryBackupEntry { SubKey = subKey, ValueName = valueName, RawValue = AbsentMarker });
                return;
            }

            var kind = key.GetValueKind(valueName);
            entries.Add(new RegistryBackupEntry
            {
                SubKey = subKey,
                ValueName = valueName,
                RawValue = Convert.ToString(val) ?? "",
                Kind = (int)kind
            });
        }
        catch { }
    }

    private static void RestoreRegistryEntry(RegistryBackupEntry entry)
    {
        try
        {
            if (entry.RawValue == AbsentMarker)
            {
                using var key = Registry.CurrentUser.OpenSubKey(entry.SubKey, writable: true);
                key?.DeleteValue(entry.ValueName, throwOnMissingValue: false);
                return;
            }

            var kind = (RegistryValueKind)entry.Kind;
            object valueToWrite = kind == RegistryValueKind.DWord && int.TryParse(entry.RawValue, out var intVal)
                ? intVal
                : entry.RawValue;
            SetRegistryValue(entry.SubKey, entry.ValueName, valueToWrite, kind);
        }
        catch { }
    }

    private static bool SubKeyExists(string subKey)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetRegistryValue(string subKey, string valueName, object value, RegistryValueKind kind)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
            key?.SetValue(valueName, value, kind);
        }
        catch { }
    }

    private static string GetDocumentsKnownFolder()
    {
        try
        {
            var guid = FolderIdDocuments;
            var hr = SHGetKnownFolderPath(ref guid, 0, IntPtr.Zero, out var ptr);
            if (hr != 0 || ptr == IntPtr.Zero) return string.Empty;
            try
            {
                return Marshal.PtrToStringUni(ptr) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SetDocumentsKnownFolder(string path)
    {
        var guid = FolderIdDocuments;
        var hr = SHSetKnownFolderPath(ref guid, 0, IntPtr.Zero, path);
        if (hr != 0)
        {
            SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Personal", path, RegistryValueKind.ExpandString);
            SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", "Personal", path, RegistryValueKind.String);
        }
    }

    private static void NotifyEnvironmentChanged()
    {
        try
        {
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "Environment", 0x0002, 150, out _);
        }
        catch { }
    }

    private static string SanitizeFolderName(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return "Arquivos_Salvos";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = username.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == Path.DirectorySeparatorChar || chars[i] == Path.AltDirectorySeparatorChar)
                chars[i] = '_';
        }

        var cleaned = new string(chars).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "Arquivos_Salvos" : cleaned;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static void LogTrace(string msg)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ApacKiosk_Trace.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [UserFolderEnv] {msg}\n");
        }
        catch { }
    }

    private sealed class RegistryBackupEntry
    {
        public string SubKey { get; set; } = "";
        public string ValueName { get; set; } = "";
        public string RawValue { get; set; } = "";
        public int Kind { get; set; } = (int)RegistryValueKind.String;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, string pszPath);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
