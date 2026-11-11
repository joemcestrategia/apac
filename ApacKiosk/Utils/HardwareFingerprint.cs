using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace ApacKiosk.Utils;

public static class HardwareFingerprint
{
    public static string Generate()
    {
        string motherboard = GetWmiValue("Win32_BaseBoard", "SerialNumber");
        string biosUuid = GetWmiValue("Win32_ComputerSystemProduct", "UUID");
        string diskSerial = GetBootDiskSerial();

        // Fallback para VMs ou hardwares genéricos
        if (string.IsNullOrWhiteSpace(motherboard) || motherboard.ToLower().Contains("o.e.m"))
            motherboard = "MB-GENERIC";

        if (string.IsNullOrWhiteSpace(biosUuid) || biosUuid.Replace("0", "").Replace("-", "").Length == 0)
            biosUuid = "BIOS-GENERIC";

        if (string.IsNullOrWhiteSpace(diskSerial))
            diskSerial = "DISK-GENERIC";

        string combined = $"{motherboard}|{biosUuid}|{diskSerial}";
        
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string GetWmiValue(string wmiClass, string wmiProperty)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {wmiProperty} FROM {wmiClass}");
            foreach (var item in searcher.Get())
            {
                var value = item[wmiProperty]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
            // Ignore WMI errors
        }
        return string.Empty;
    }

    private static string GetBootDiskSerial()
    {
        try
        {
            // Tenta pegar o disco de índice 0, que geralmente é o de boot
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE Index = 0");
            foreach (var item in searcher.Get())
            {
                var val = item["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
            
            // Fallback: pega o primeiro que retornar serial
            using var searcherAny = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
            foreach (var item in searcherAny.Get())
            {
                var val = item["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        catch
        {
            // Ignore WMI errors
        }
        return string.Empty;
    }
}
