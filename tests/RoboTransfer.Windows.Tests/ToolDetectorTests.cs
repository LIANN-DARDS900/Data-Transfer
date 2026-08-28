using RoboTransfer.Core;
using RoboTransfer.Robocopy;
using RoboTransfer.Usmt;
using Xunit;
namespace RoboTransfer.Windows.Tests;
public sealed class ToolDetectorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"RoboTransfer-{Guid.NewGuid():N}");
    public ToolDetectorTests() => Directory.CreateDirectory(root);
    [Fact] public void Missing_robocopy_is_unavailable() => Assert.Equal(CapabilityState.NotAvailable, RobocopyDetector.DetectAtPath(Path.Combine(root, "robocopy.exe")).State);
    [Fact] public void Missing_usmt_is_unavailable() => Assert.Equal(CapabilityState.NotAvailable, UsmtToolDetector.DetectAtRoots([root]).State);
    [Fact] public void Partial_usmt_install_is_unavailable() { var path = Path.Combine(root, "Windows Kits", "10", "Assessment and Deployment Kit", "User State Migration Tool", "amd64"); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "scanstate.exe"), "test"); Assert.Equal(CapabilityState.NotAvailable, UsmtToolDetector.DetectAtRoots([root]).State); }
    public void Dispose() => Directory.Delete(root, true);
}
