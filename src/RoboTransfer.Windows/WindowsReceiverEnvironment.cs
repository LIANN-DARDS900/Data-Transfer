using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using RoboTransfer.Core;
namespace RoboTransfer.Windows;

public sealed class WindowsReceiverEnvironment : IReceiverEnvironment
{
    public string MachineName => Environment.MachineName;
    public bool IsLocalHost(string host)
    {
        // No discovery or DNS/network probes. Require the local computer name or its known FQDN.
        var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        return string.Equals(host, MachineName, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(domain) && string.Equals(host, MachineName + "." + domain, StringComparison.OrdinalIgnoreCase));
    }
    public string GetAccessPath(string validatedUncPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Receiver network access requires Windows.");
        return ReceiverInboxPaths.Normalize(validatedUncPath);
    }
    public long? GetAvailableBytes(string validatedUncPath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        return GetDiskFreeSpaceEx(ReceiverInboxPaths.Normalize(validatedUncPath) + "\\", out var available, out _, out _) && available <= long.MaxValue ? (long)available : null;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);
}
