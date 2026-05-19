namespace ApacKiosk.Services;

public class LogCleanupService
{
    private CancellationTokenSource? _cts;
    private Task? _cleanupTask;

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _cleanupTask = CleanupLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { CleanupOldLogs(); } catch { }
            try { CheckLogFolderSize(); } catch { }
            try { await Task.Delay(600_000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void CleanupOldLogs()
    {
        int retentionDays = int.Parse(Data.DatabaseHelper.GetConfig("log_retention_days", "30"));
        var cutoff = DateTime.Now.AddDays(-retentionDays);

        var oldEntries = Data.DatabaseHelper.Query<Models.LogEntry>(
            "SELECT * FROM log_entries WHERE timestamp < @c", new { c = cutoff });

        foreach (var entry in oldEntries)
        {
            if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
            {
                try { File.Delete(entry.FilePath); } catch { }
            }
        }

        Data.DatabaseHelper.Execute(
            "DELETE FROM log_entries WHERE timestamp < @c", new { c = cutoff });
    }

    private void CheckLogFolderSize()
    {
        double maxGb = double.Parse(Data.DatabaseHelper.GetConfig("max_log_size_gb", "10"));
        double maxBytes = maxGb * 1024 * 1024 * 1024;
        double threshold = maxBytes * 0.8;

        var logDirs = new[] { "Logs\\Screenshots", "Logs\\Camera", "Logs\\Keylogs" };
        long totalSize = 0;

        foreach (var dir in logDirs)
        {
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { totalSize += new FileInfo(file).Length; } catch { }
                }
            }
        }

        if (totalSize > threshold)
        {
            Data.DatabaseHelper.InsertLog("system_event", null, null,
                $"Alerta: pasta de logs atingiu {(totalSize / 1024.0 / 1024 / 1024):F1} GB de {(maxGb)} GB");
        }
    }
}
