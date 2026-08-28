using RoboTransfer.Core;
namespace RoboTransfer.Usmt;
public sealed class UsmtToolDetector : IToolDetector
{
    public Task<ToolCapability> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new ToolCapability("USMT", CapabilityState.NotAvailable, Detail: "USMT detection requires Windows."));
        var roots = new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }.Where(root => !string.IsNullOrWhiteSpace(root)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(DetectAtRoots(roots));
    }
    public static ToolCapability DetectAtRoots(IEnumerable<string> programFilesRoots)
    {
        foreach (var root in programFilesRoots)
        {
            var kit = Path.Combine(root, "Windows Kits", "10", "Assessment and Deployment Kit", "User State Migration Tool"); if (!Directory.Exists(kit)) continue;
            foreach (var architecture in new[] { "amd64", "x86", "arm64" })
            {
                var path = Path.Combine(kit, architecture);
                if (File.Exists(Path.Combine(path, "scanstate.exe")) && File.Exists(Path.Combine(path, "loadstate.exe"))) return new("USMT", CapabilityState.Available, path, Detail: "ScanState and LoadState detected; neither was executed.");
            }
        }
        return new("USMT", CapabilityState.NotAvailable, Detail: "A complete installed Windows ADK USMT pair was not detected.");
    }
}
