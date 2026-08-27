using System.Diagnostics;
using RoboTransfer.Core;
namespace RoboTransfer.Robocopy;
public sealed class RobocopyDetector : IToolDetector
{
    public Task<ToolCapability> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new ToolCapability("Robocopy", CapabilityState.NotAvailable, Detail: "Robocopy detection requires Windows."));
        var root = Environment.GetEnvironmentVariable("SystemRoot");
        var path = root is null ? null : Path.Combine(root, "System32", "robocopy.exe");
        if (path is null || !File.Exists(path)) return Task.FromResult(new ToolCapability("Robocopy", CapabilityState.NotAvailable, Detail: "The system Robocopy executable was not detected."));
        string? version = null;
        try { version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Task.FromResult(new ToolCapability("Robocopy", CapabilityState.Available, path, version, "System Robocopy detected; no operation was executed."));
    }
}
