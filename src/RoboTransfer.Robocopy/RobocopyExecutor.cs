using System.Diagnostics;
using RoboTransfer.Core;

namespace RoboTransfer.Robocopy;

public sealed record ProcessExecutionResult(int ExitCode, bool Cancelled, IReadOnlyList<string> StandardOutput, IReadOnlyList<string> StandardError, DateTimeOffset StartedAt, DateTimeOffset EndedAt);
public interface IRobocopyProcessRunner { Task<ProcessExecutionResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken); }
public sealed class BoundedLineBuffer(int capacity)
{
    private readonly Queue<string> lines = new(capacity); private readonly object gate = new();
    public void Add(string? line) { if (line is null) return; lock (gate) { if (lines.Count == capacity) lines.Dequeue(); lines.Enqueue(line); } }
    public IReadOnlyList<string> Snapshot() { lock (gate) return lines.ToArray(); }
}
public sealed class RobocopyExecutor(IRobocopyProcessRunner? runner = null) : IOperationalTransferEngine
{
    public async Task<TransferResult> ExecuteAsync(MigrationExecutionRequest request, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var executable = Path.GetFullPath(request.Plan.RobocopyExecutablePath); if (!File.Exists(executable)) return Failed(ErrorCategory.ToolUnavailable, "Robocopy is no longer available at the reviewed system path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }; foreach (var argument in RobocopyArgumentPolicy.Build(request)) start.ArgumentList.Add(argument);
        try { var result = await (runner ?? new SystemRobocopyProcessRunner()).RunAsync(start, cancellationToken); if (result.Cancelled) return new(false, true, 0, 0, [new(ErrorCategory.Cancelled, "Transfer was deliberately interrupted. Resume requires full revalidation.")]); var interpreted = RobocopyExitCode.Interpret(result.ExitCode); progress?.Report(new(0, 0, interpreted.Outcome.ToString(), request.SourceKnownFolder, Elapsed: result.EndedAt - result.StartedAt)); return interpreted.Succeeded ? new(true, false, 0, 0, []) : Failed(ErrorCategory.ProcessFailure, $"Robocopy reported a transfer failure (exit class {result.ExitCode})."); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return Failed(ErrorCategory.ProcessFailure, "Robocopy process execution failed."); }
        static TransferResult Failed(ErrorCategory category, string message) => new(false, false, 0, 0, [new(category, message)]);
    }
}
public sealed class SystemRobocopyProcessRunner : IRobocopyProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true }; var output = new BoundedLineBuffer(64); var error = new BoundedLineBuffer(64); var began = DateTimeOffset.UtcNow;
        process.OutputDataReceived += (_, e) => output.Add(e.Data); process.ErrorDataReceived += (_, e) => error.Add(e.Data); if (!process.Start()) throw new InvalidOperationException("Process did not start."); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var registration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }); await process.WaitForExitAsync(CancellationToken.None); return new(process.ExitCode, cancellationToken.IsCancellationRequested, output.Snapshot(), error.Snapshot(), began, DateTimeOffset.UtcNow);
    }
}
