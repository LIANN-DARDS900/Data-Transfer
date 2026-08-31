using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoboTransfer.Core;

namespace RoboTransfer.Persistence;

public sealed class MigrationReportGenerator : IReportGenerator
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public async Task<(string JsonPath, string HtmlPath)> GenerateAsync(MigrationReport report, string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory); var stem = $"RoboTransfer-{report.SessionId:N}"; var jsonPath = Path.Combine(outputDirectory, stem + ".json"); var htmlPath = Path.Combine(outputDirectory, stem + ".html"); var safe = report with { Destination = DiagnosticsRedactor.RedactPath(report.Destination), SourceProfile = DiagnosticsRedactor.RedactIdentity(report.SourceProfile) };
        await AtomicWriteAsync(jsonPath, JsonSerializer.Serialize(safe, ReportJsonOptions), cancellationToken);
        var rows = new (string, string)[] { ("Session", safe.SessionId.ToString()), ("Final status", safe.FinalStatus.ToString()), ("Source machine", safe.SourceMachine), ("Source profile", safe.SourceProfile), ("Destination", safe.Destination), ("Route", safe.Route.ToString()), ("Strategy", safe.Strategy.ToString()), ("Expected", $"{safe.ExpectedFiles:N0} files · {safe.ExpectedBytes:N0} bytes"), ("Transferred", $"{safe.TransferredFiles:N0} files · {safe.TransferredBytes:N0} bytes"), ("Cloud skipped", safe.SkippedCloudContent.ToString("N0")), ("Locked files", safe.LockedFiles.ToString("N0")), ("Conflicts", safe.Conflicts.ToString("N0")), ("Failures", safe.Failures.ToString("N0")), ("Standard verification", safe.StandardVerification.ToString()), ("Strong verification", safe.StrongVerification?.ToString() ?? "Not required"), ("Verification elapsed", safe.VerificationElapsed.ToString()), ("Policy fingerprint", safe.PolicyFingerprint), ("Execution plan fingerprint", safe.ExecutionPlanFingerprint) };
        var body = string.Join("", rows.Select(row => $"<tr><th>{WebUtility.HtmlEncode(row.Item1)}</th><td>{WebUtility.HtmlEncode(row.Item2)}</td></tr>")); var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>RoboTransfer migration report</title><style>body{{font:14px Segoe UI,Arial;color:#172b3a;margin:40px}}h1{{font-size:24px}}.status{{padding:10px;background:#eef5f7;border-left:4px solid #28758c}}table{{border-collapse:collapse;width:100%;margin-top:24px}}th,td{{text-align:left;padding:9px;border-bottom:1px solid #d9e1e5}}th{{width:260px;color:#49616f}}footer{{margin-top:28px;color:#657985}}</style></head><body><h1>RoboTransfer migration report</h1><p class=\"status\">{WebUtility.HtmlEncode(safe.FinalStatus.ToString())}</p><table>{body}</table><footer>Schema {safe.SchemaVersion} · Generated {safe.CreatedAt:O} · Verification result {safe.VerificationResultIdentity}</footer></body></html>"; await AtomicWriteAsync(htmlPath, html, cancellationToken); return (jsonPath, htmlPath);
    }
    private static async Task AtomicWriteAsync(string path, string content, CancellationToken token) { var temporary = path + $".{Guid.NewGuid():N}.tmp"; try { await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), token); File.Move(temporary, path, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); } }
}

public static class DiagnosticsRedactor
{
    public static string RedactPath(string value) { if (string.IsNullOrWhiteSpace(value)) return "[not recorded]"; var root = Path.GetPathRoot(value); return string.IsNullOrWhiteSpace(root) ? "[redacted path]" : root + "…"; }
    public static string RedactIdentity(string value) => string.IsNullOrWhiteSpace(value) ? "[not recorded]" : $"user-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12]}";
    public static string RedactMessage(string value) { var result = value; foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(token => token.Contains(@":\", StringComparison.Ordinal) || token.StartsWith(@"\\", StringComparison.Ordinal))) result = result.Replace(token, "[redacted path]", StringComparison.Ordinal); return result; }
}
