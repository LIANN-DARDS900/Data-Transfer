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
    [Fact] public async Task Canonical_system_robocopy_never_bypasses_trust_policy_on_windows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var robocopy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "robocopy.exe");
        var result = await new WindowsExecutableTrustValidator().ValidateAsync(robocopy, true, TestContext.Current.CancellationToken);

        Assert.NotEqual(ExecutableTrustStatus.InvalidLocation, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));

        if (result.IsAuthorized)
        {
            Assert.Equal(ExecutableTrustStatus.Trusted, result.Status);
            Assert.Contains("Microsoft", result.Publisher ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.Contains(
            result.Status,
            new[] { ExecutableTrustStatus.Unavailable, ExecutableTrustStatus.NotTrusted, ExecutableTrustStatus.InvalidIdentity });
    }
    [Fact]
    public async Task Windows_valid_system_signature_is_accepted_by_robocopy_validator()
    {
        if (!OperatingSystem.IsWindows()) return;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var robocopy = Path.Combine(windows, "System32", "robocopy.exe");
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[string](Get-AuthenticodeSignature -LiteralPath $env:ROBOTRANSFER_TEST_PATH).Status");
        start.Environment["ROBOTRANSFER_TEST_PATH"] = robocopy;
        using var process = System.Diagnostics.Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("Valid", (await output).Trim());
        Assert.True(string.IsNullOrWhiteSpace(await error));
        var result = await new WindowsExecutableTrustValidator().ValidateAsync(robocopy, true, timeout.Token);
        Assert.True(result.IsAuthorized, result.Explanation);
        Assert.Contains("Microsoft", result.Publisher ?? "", StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void Version_metadata_is_not_blank() => Assert.False(string.IsNullOrWhiteSpace(ApplicationIdentity.Version));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(root + "-target")) Directory.Delete(root + "-target", true); }
}
