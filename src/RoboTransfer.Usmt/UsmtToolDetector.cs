using RoboTransfer.Core;
namespace RoboTransfer.Usmt;
public sealed class UsmtToolDetector : IToolDetector
{
    public Task<ToolCapability> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new ToolCapability("USMT", CapabilityState.NotAvailable, Detail: "USMT detection requires Windows."));
        var roots = new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
        foreach (var root in roots)
        {
            var kit = Path.Combine(root!, "Windows Kits", "10", "Assessment and Deployment Kit", "User State Migration Tool");
            if (!Directory.Exists(kit)) continue;
            foreach (var arch in new[] { "amd64", "x86", "arm64" })
            {
                var path = Path.Combine(kit, arch); var scan = Path.Combine(path, "scanstate.exe"); var load = Path.Combine(path, "loadstate.exe");
                if (File.Exists(scan) && File.Exists(load)) return Task.FromResult(new ToolCapability("USMT", CapabilityState.Available, path, Detail: "ScanState and LoadState detected; neither was executed."));
            }
        }
        return Task.FromResult(new ToolCapability("USMT", CapabilityState.NotAvailable, Detail: "An installed Windows ADK USMT pair was not detected."));
    }
}
