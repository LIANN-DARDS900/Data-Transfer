using RoboTransfer.Core;
using RoboTransfer.Robocopy;
using Xunit;

namespace RoboTransfer.Core.Tests;

public sealed class ProductionReadinessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "RoboTransferTests", Guid.NewGuid().ToString("N"));
    [Fact] public void Application_layout_separates_durable_state()
    {
        var layout = ApplicationDataLayout.Create(root);
        var paths = new[] { layout.Sessions, layout.Manifests, layout.Plans, layout.Verification, layout.Reports, layout.Diagnostics, layout.Policies };
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(paths, path => Assert.StartsWith(Path.GetFullPath(root), path, StringComparison.OrdinalIgnoreCase));
        Assert.All(paths, path => Assert.True(Directory.Exists(path)));
    }
    [Fact] public void Empty_session_identity_is_rejected() => Assert.Throws<ArgumentException>(() => ApplicationDataLayout.Create(root).GetSessionDirectory(Guid.Empty));
    [Fact] public void Reparse_state_root_is_rejected_when_supported()
    {
        Directory.CreateDirectory(root); var target = root + "-target"; Directory.CreateDirectory(target); var link = Path.Combine(root, "link");
        try { Directory.CreateSymbolicLink(link, target); } catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        Assert.Throws<InvalidDataException>(() => ApplicationDataLayout.Create(link));
    }
    [Fact] public async Task Trust_validation_distinguishes_non_windows_unavailability()
    {
        if (OperatingSystem.IsWindows()) return;
        var result = await new WindowsExecutableTrustValidator().ValidateAsync("robocopy.exe", true, TestContext.Current.CancellationToken);
        Assert.Equal(ExecutableTrustStatus.Unavailable, result.Status); Assert.False(result.IsAuthorized);
    }
    [Fact] public void Version_metadata_is_not_blank() => Assert.False(string.IsNullOrWhiteSpace(ApplicationIdentity.Version));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(root + "-target")) Directory.Delete(root + "-target", true); }
}
