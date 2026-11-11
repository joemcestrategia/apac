using System;
using System.Collections.Generic;

namespace ApacKiosk.Services;

internal static class TrustedProcessNames
{
    private static readonly HashSet<string> WebViewRuntimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedgewebview2.exe"
    };

    public static bool IsWebViewRuntime(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

        return WebViewRuntimeNames.Contains(normalized);
    }
}
