using System.Text.Json;
using System.Text.Json.Serialization;
using RoboTransfer.Core;
namespace RoboTransfer.Persistence;

public sealed class JsonPolicyProvider(string path) : IPolicyProvider
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowTrailingCommas = false, ReadCommentHandling = JsonCommentHandling.Disallow, PropertyNameCaseInsensitive = false }; options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)); return options; }
    public async Task<PolicyLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return PolicyLoadResult.Invalid(path, new PolicyValidationIssue("policy", "The policy file does not exist. Conservative policy remains active."));
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var policy = await JsonSerializer.DeserializeAsync<PolicyProfile>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (policy is null) return PolicyLoadResult.Invalid(path, new PolicyValidationIssue("policy", "The policy document is empty."));
            var issues = Validate(policy);
            return issues.Count == 0 ? new(true, policy, issues, path) : new(false, PolicyProfile.Conservative, issues, path);
        }
        catch (JsonException ex) { return PolicyLoadResult.Invalid(path, new PolicyValidationIssue("policy", $"Malformed JSON at line {ex.LineNumber}, byte {ex.BytePositionInLine}.")); }
        catch (UnauthorizedAccessException) { return PolicyLoadResult.Invalid(path, new PolicyValidationIssue("policy", "The policy file cannot be read with the current identity.")); }
        catch (IOException ex) { return PolicyLoadResult.Invalid(path, new PolicyValidationIssue("policy", $"The policy file could not be read ({ex.GetType().Name}).")); }
    }

    public static IReadOnlyList<PolicyValidationIssue> Validate(PolicyProfile policy)
    {
        var issues = new List<PolicyValidationIssue>();
        if (policy.SchemaVersion != PolicyProfile.CurrentSchemaVersion) issues.Add(new("schemaVersion", $"Unsupported policy schema {policy.SchemaVersion}; expected {PolicyProfile.CurrentSchemaVersion}."));
        if (policy.AllowConfiguredNetworkShare && policy.ApprovedNetworkSharePaths.Count == 0) issues.Add(new("approvedNetworkSharePaths", "At least one approved UNC path is required when network-share migration is enabled."));
        foreach (var path in policy.ApprovedNetworkSharePaths)
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsUnc) issues.Add(new("approvedNetworkSharePaths", "Every approved network location must be an absolute UNC path."));
        if (policy.DefaultConflictPolicy == ConflictPolicy.Replace) issues.Add(new("defaultConflictPolicy", "Replace cannot be the unattended default because it may destroy destination data."));
        return issues;
    }
}
