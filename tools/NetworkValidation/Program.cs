using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
using RoboTransfer.Robocopy;
using RoboTransfer.Verification;
using RoboTransfer.Windows;

// Synthetic data only. Never executes test files or changes network/security configuration.
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows is required.");
string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
var ciLocal = args.Contains("--ci-local");
if (ciLocal && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Local fixtures are restricted to GitHub CI.");
var output = Path.GetFullPath(Option("--output") ?? Path.Combine(Path.GetTempPath(), "RoboTransfer-Qualification", Guid.NewGuid().ToString("N")));
var sizeMiB = int.Parse(Option("--size-mib") ?? "64", System.Globalization.CultureInfo.InvariantCulture);
if (sizeMiB is < 1 or > 4096) throw new ArgumentException("Size must be 1..4096 MiB.");
var approved = Option("--destination");
var policyPath = Option("--policy");
if (!ciLocal)
{
    if (approved is null || policyPath is null || !Uri.TryCreate(approved, UriKind.Absolute, out var uri) || !uri.IsUnc)
        throw new ArgumentException("Supply --destination \\\\NEWPC\\ApprovedShare and --policy approved-policy.json.");
    var loaded = await new JsonPolicyProvider(policyPath).LoadAsync();
    if (!loaded.IsValid || !loaded.Policy.AllowConfiguredNetworkShare || !loaded.Policy.AllowRobocopy ||
        !loaded.Policy.ApprovedNetworkSharePaths.Any(p => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(approved)), StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("The exact share and copy tool must already be authorized by the supplied policy.");
}
void RejectReparse(string path)
{
    for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Qualification paths cannot traverse reparse points.");
}
// Policy is checked before touching the remote destination. No subnet discovery or listeners.
var runId = Guid.NewGuid();
RejectReparse(output);
Directory.CreateDirectory(output);
var source = Path.Combine(output, "synthetic-source");
if (Directory.Exists(source)) throw new IOException("Choose a new output directory for each run.");
Directory.CreateDirectory(source);
var destination = ciLocal ? Path.Combine(output, "local-destination") : Path.Combine(approved!, "RoboTransfer-Qualification-" + runId.ToString("N"));
RejectReparse(destination);
if (Directory.Exists(destination)) throw new IOException("Qualification destination must be new.");
var rows = new List<Scenario>();
var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
void Observe(string scenario, string status, string detail) { rows.Add(new(scenario, status, detail)); Console.WriteLine($"{status}: {scenario}: {detail}"); }
var elapsed = TimeSpan.Zero;
long copied = 0;
bool criticalFailure = false;
try
{
    // Inert bytes with representative extensions; these are not valid Office/media/executable samples.
    foreach (var ext in new[] { ".txt", ".pdf", ".docx", ".xlsx", ".jpg", ".png", ".zip", ".mp4", ".pst", ".exe", ".ps1", ".bat", ".ost" })
        await File.WriteAllTextAsync(Path.Combine(source, "inert-fixture" + ext), "RoboTransfer synthetic fixture. Never execute. " + ext);
    await File.WriteAllBytesAsync(Path.Combine(source, "zero.bin"), []);
    var unicode = Path.Combine(source, "Données العربية"); Directory.CreateDirectory(unicode);
    await File.WriteAllTextAsync(Path.Combine(unicode, "résumé.txt"), "Unicode filename payload");
    for (var i = 0; i < 100; i++) await File.WriteAllTextAsync(Path.Combine(source, $"small-{i:D3}.txt"), new string('s', 1024));
    await using (var big = new FileStream(Path.Combine(source, "large-random.bin"), FileMode.CreateNew, FileAccess.Write))
    {
        var buffer = new byte[1048576];
        for (var i = 0; i < sizeMiB; i++) { RandomNumberGenerator.Fill(buffer); await big.WriteAsync(buffer); }
    }
    var manifest = Path.Combine(output, "manifest.jsonl");
    ManifestScanResult scan;
    await using (var writer = new JsonLinesManifestWriter(manifest))
        scan = await new ManifestScanner(new WindowsCloudPlaceholderDetector()).ScanAsync(new(runId, "synthetic", [new(KnownFolderKind.Documents, source, true, KnownFolderResolution.Resolved, "Synthetic fixture only")], VerificationLevel.Strong, ConflictPolicy.KeepBoth, manifest), writer);
    var reader = new JsonLinesManifestReader();
    var footer = (await reader.InspectAsync(manifest)).Footer!;
    var plan = new MigrationExecutionPlan(runId, DateTimeOffset.UtcNow, Environment.MachineName, "synthetic", [KnownFolderKind.Documents], "synthetic-manifest", manifest, footer.EligibleEntryCount, footer.EligibleBytes, ciLocal ? MigrationRoute.ExternalStorage : MigrationRoute.ConfiguredNetworkShare, MigrationStrategy.RobocopyKnownFolders, "synthetic", destination, long.MaxValue, ConflictPolicy.KeepBoth, "skip unavailable", VerificationLevel.Strong, 1, "qualification-only", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "robocopy.exe"), "system", ApplicationIdentity.Version);
    var request = new MigrationExecutionRequest(plan, source, Path.Combine(destination, "Documents"), KnownFolderKind.Documents);
    var clock = Stopwatch.StartNew();
    var transfer = await new KeepBothTransferEngine(reader).ExecuteAsync(request);
    clock.Stop(); elapsed = clock.Elapsed; copied = transfer.BytesTransferred;
    var reconciliation = await new ManifestTransferReconciler(reader).ReconcileAsync(plan, transfer);
    Observe("Default engine copy and reconciliation", reconciliation.State == TransferCompletionState.TransferCompletedVerificationPending ? "PASS" : "FAIL", $"{transfer.FilesTransferred} files; {copied} logical bytes; {elapsed.TotalSeconds:F3}s; {reconciliation.State}");
    criticalFailure |= !transfer.Succeeded;
    var verifyRequest = new VerificationRequest(plan, plan.Fingerprint, plan.PolicyFingerprint, new Dictionary<KnownFolderKind, string> { [KnownFolderKind.Documents] = source }, destination, VerificationLevel.Strong);
    var verified = await new FileVerificationEngine(reader).VerifyAsync(verifyRequest);
    await File.WriteAllTextAsync(Path.Combine(output, "per-file-verification.json"), JsonSerializer.Serialize(verified, options));
    var complete = verified.VerifiedFiles == footer.EligibleEntryCount && verified.VerifiedBytes == footer.EligibleBytes;
    Observe("SHA-256 completion", complete ? "PASS" : "FAIL", $"{verified.VerifiedFiles}/{footer.EligibleEntryCount} files verified; {verified.State}; verification is separate from copying.");
    criticalFailure |= !complete;
    Observe("OST exclusion", !File.Exists(Path.Combine(request.DestinationRoot, "inert-fixture.ost")) ? "PASS" : "FAIL", $"Scan exclusions: {scan.Skipped}. OST exclusion is an application rule, not evidence of endpoint security blocking an extension.");

    var tamperSource = Path.Combine(source, "inert-fixture.txt");
    var tamperDestination = Path.Combine(request.DestinationRoot, "inert-fixture.txt");
    var content = await File.ReadAllBytesAsync(tamperSource); var altered = (byte[])content.Clone(); altered[0] ^= 1;
    await File.WriteAllBytesAsync(tamperDestination, altered); File.SetLastWriteTimeUtc(tamperDestination, File.GetLastWriteTimeUtc(tamperSource));
    var tampered = await new FileVerificationEngine(reader).VerifyAsync(verifyRequest);
    Observe("Same-size corruption detected", tampered.Entries.Any(e => e.Status == VerificationEntryStatus.ContentMismatch) && tampered.Mismatches > 0 ? "PASS" : "FAIL", $"Mismatch count={tampered.Mismatches}");
    File.Copy(tamperSource, tamperDestination, true); // Only this run's synthetic file.

    using var cancelled = new CancellationTokenSource();
    var interruptedRoot = Path.Combine(destination, "interruption-fixture");
    var interruptedRequest = request with { DestinationRoot = Path.Combine(interruptedRoot, "Documents") };
    var interrupting = new KeepBothTransferEngine(reader, async (_, to, token) => { await to.WriteAsync(new byte[1024], token); cancelled.Cancel(); token.ThrowIfCancellationRequested(); });
    var interrupted = await interrupting.ExecuteAsync(interruptedRequest, cancellationToken: cancelled.Token);
    Observe("Injected cancellation and owned partial cleanup", interrupted.Cancelled && !Directory.EnumerateFiles(interruptedRoot, "*", SearchOption.AllDirectories).Any() ? "PASS" : "FAIL", "Deterministic injected cancellation; no cable or firewall fault was injected.");
    var restarted = await new KeepBothTransferEngine(reader).ExecuteAsync(interruptedRequest);
    var restartVerification = await new FileVerificationEngine(reader).VerifyAsync(verifyRequest with { DestinationRoot = interruptedRoot });
    Observe("Retry after cancellation", restarted.Succeeded && restartVerification.VerifiedFiles == footer.EligibleEntryCount ? "PASS" : "FAIL", "Retransfers complete files; does not prove byte-offset resume or recovery after unplugging Ethernet.");

    var repeated = await new KeepBothTransferEngine(reader).ExecuteAsync(request);
    var duplicates = Directory.EnumerateFiles(request.DestinationRoot, "*RoboTransfer copy*", SearchOption.AllDirectories).Count();
    Observe("Idempotent resume requirement", duplicates == 0 ? "PASS" : "FAIL", $"Characterization: rerunning KeepBoth produced {duplicates} additional copies. Resume currently restarts work and is not idempotent. Copy result={repeated.Succeeded}.");

    var renamed = Path.Combine(request.DestinationRoot, "inert-fixture (RoboTransfer copy).txt");
    if (File.Exists(renamed))
    {
        await File.WriteAllBytesAsync(renamed, altered); File.SetLastWriteTimeUtc(renamed, File.GetLastWriteTimeUtc(tamperSource));
        var collisionVerification = await new FileVerificationEngine(reader).VerifyAsync(verifyRequest);
        Observe("Receipt-accurate collision verification", collisionVerification.Entries.Any(e => e.Status == VerificationEntryStatus.ContentMismatch) ? "PASS" : "FAIL", "Characterization: latest renamed copy was deliberately corrupted while the original remained correct. Verifier currently selects an older metadata match; durable destination receipts are required.");
        File.Copy(tamperSource, renamed, true);
    }
    var pst = Path.Combine(source, "inert-fixture.pst");
    await using (var locked = new FileStream(pst, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var lockedResult = await new KeepBothTransferEngine(reader).ExecuteAsync(request with { DestinationRoot = Path.Combine(destination, "lock-fixture") });
        Observe("Locked PST classification", !lockedResult.Succeeded && lockedResult.Errors.Any(e => e.Category == ErrorCategory.FileLocked) ? "PASS" : "FAIL", "Exclusive OS file lock on an inert PST fixture, not an Outlook integration test.");
    }

    var roboSource = Path.Combine(output, "robocopy-source"); Directory.CreateDirectory(roboSource);
    await File.WriteAllBytesAsync(Path.Combine(roboSource, "data.bin"), content);
    var tool = await new RobocopyDetector(new WindowsExecutableTrustValidator()).DetectAsync();
    if (tool.State == CapabilityState.Available && tool.ExecutablePath is not null)
    {
        var roboDestination = Path.Combine(destination, "robocopy-adapter");
        var result = await new RobocopyExecutor().ExecuteAsync(new(plan with { ConflictPolicy = ConflictPolicy.Skip, RobocopyExecutablePath = tool.ExecutablePath }, roboSource, roboDestination, KnownFolderKind.Documents));
        var valid = result.Succeeded && File.Exists(Path.Combine(roboDestination, "data.bin")) && (await File.ReadAllBytesAsync(Path.Combine(roboDestination, "data.bin"))).SequenceEqual(content);
        Observe("Actual system Robocopy adapter", valid ? "PASS" : "FAIL", "Independent adapter smoke test; /Z configured. Default UI uses KeepBoth, not this adapter.");
    }
    else Observe("Actual system Robocopy adapter", "BLOCKED", tool.Detail ?? "Trusted system tool unavailable.");
    Observe("TCP 445 restriction, bad credentials and cable loss", "NOT RUN", "Requires two authorized Windows endpoints and a controlled lab. No host policy or credentials were modified.");
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    criticalFailure = true;
    Observe("Qualification execution", "BLOCKED", $"{ex.GetType().Name}, Windows error {ex.HResult & 0xffff}. Investigate locally; do not infer an EDR block from an I/O error.");
}
finally
{
    var rate = elapsed.TotalSeconds > 0 ? copied / elapsed.TotalSeconds : (double?)null;
    var summary = new { SchemaVersion = 1, RunId = runId, Utc = DateTimeOffset.UtcNow, Machine = Environment.MachineName, Environment = ciLocal ? "ONE Windows runner, local filesystem" : "Explicit approved UNC; physical topology must be confirmed separately", ApplicationVersion = ApplicationIdentity.Version, SourceMiB = sizeMiB, CopiedBytes = copied, CopySeconds = elapsed.TotalSeconds, EffectiveMiBPerSecond = rate / 1048576d, RateScope = "Logical bytes copied / first default-engine copy elapsed; excludes scan and SHA-256; not wire throughput", PhysicalDellSpeed = "NOT MEASURED unless this run was performed on the physical Dell pair", Findings = rows };
    await File.WriteAllTextAsync(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(summary, options));
    var html = "<!doctype html><meta charset=utf-8><title>RoboTransfer qualification</title><style>body{font:16px system-ui;margin:40px;max-width:1100px}td,th{text-align:left;padding:12px;border-bottom:1px solid #ccc}table{border-collapse:collapse}</style><h1>RoboTransfer qualification</h1><p>" + WebUtility.HtmlEncode(summary.Environment) + "</p><p>First copy effective rate: " + (rate / 1048576d)?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MiB/s. " + WebUtility.HtmlEncode(summary.RateScope) + "</p><table><tr><th>Scenario</th><th>Result</th><th>Evidence / limit</th></tr>" + string.Join("", rows.Select(r => $"<tr><td>{WebUtility.HtmlEncode(r.Name)}</td><td>{r.Status}</td><td>{WebUtility.HtmlEncode(r.Detail)}</td></tr>")) + "</table><p>Known requirement failures remain FAIL even if the characterization harness itself exits successfully. Synthetic fixtures are retained; remove only this run's directories after reviewing evidence.</p>";
    await File.WriteAllTextAsync(Path.Combine(output, "qualification.html"), html, Encoding.UTF8);
    Console.WriteLine("REPORT: " + Path.Combine(output, "qualification.json"));
}
return criticalFailure ? 1 : 0;

internal sealed record Scenario(string Name, string Status, string Detail);
