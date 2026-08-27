using System.Diagnostics;
using RoboTransfer.Core;
namespace RoboTransfer.Robocopy;
public sealed class RobocopyDetector : IToolDetector
{
    public Task<ToolCapability> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new ToolCapability("Robocopy", CapabilityState.NotAvailable, Detail: "Robocopy detection requires Windows."));
        var root = Environment.GetEnvironmentVariable("SystemRoot"); return Task.FromResult(DetectAtPath(root is null ? null : Path.Combine(root, "System32", "robocopy.exe")));
    }
    public static ToolCapability DetectAtPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new("Robocopy", CapabilityState.NotAvailable, Detail: "The system Robocopy executable was not detected.");
        string? version = null; try { version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return new("Robocopy", CapabilityState.Available, path, version, "System Robocopy detected; no operation was executed.");
    }
}
