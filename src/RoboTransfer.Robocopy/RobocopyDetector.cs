using System.Diagnostics;
using RoboTransfer.Core;
namespace RoboTransfer.Robocopy;
public sealed class RobocopyDetector(IExecutableTrustValidator? trustValidator = null) : IToolDetector
{
    public async Task<ToolCapability> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new ToolCapability("Robocopy", CapabilityState.NotAvailable, Detail: "Robocopy detection requires Windows.");
        var root = Environment.GetEnvironmentVariable("SystemRoot"); var detected = DetectAtPath(root is null ? null : Path.Combine(root, "System32", "robocopy.exe"));
        if (detected.State != CapabilityState.Available || trustValidator is null || detected.ExecutablePath is null) return detected;
        var trust = await trustValidator.ValidateAsync(detected.ExecutablePath, requireMicrosoftPublisher: true, cancellationToken);
        return trust.IsAuthorized ? detected with { Detail = trust.Explanation } : detected with { State = trust.Status == ExecutableTrustStatus.Unavailable ? CapabilityState.Unknown : CapabilityState.ForbiddenByPolicy, Detail = trust.Explanation };
    }
    public static ToolCapability DetectAtPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new("Robocopy", CapabilityState.NotAvailable, Detail: "The system Robocopy executable was not detected.");
        string? version = null; try { version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return new("Robocopy", CapabilityState.Available, path, version, "System Robocopy detected; no operation was executed.");
    }
}
