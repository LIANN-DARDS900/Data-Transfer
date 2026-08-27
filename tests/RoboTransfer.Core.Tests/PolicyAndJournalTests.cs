using Microsoft.Extensions.Logging.Abstractions;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
using Xunit;
namespace RoboTransfer.Core.Tests;
public sealed class PolicyAndJournalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"RoboTransfer-{Guid.NewGuid():N}");
    public PolicyAndJournalTests() => Directory.CreateDirectory(root);
    [Fact] public async Task Malformed_policy_fails_closed() { var path = Path.Combine(root, "policy.json"); await File.WriteAllTextAsync(path, "{ invalid"); var result = await new JsonPolicyProvider(path).LoadAsync(); Assert.False(result.IsValid); Assert.Equal(PolicyProfile.Conservative, result.Policy); }
    [Fact] public async Task Unsupported_policy_version_fails_closed() { var path = Path.Combine(root, "policy.json"); await File.WriteAllTextAsync(path, """{"schemaVersion":99,"allowConfiguredNetworkShare":false,"allowExternalStorage":false,"allowUsmt":false,"allowRobocopy":false,"requiredVerification":"strong","defaultConflictPolicy":"keepBoth","approvedNetworkSharePaths":[]}"""); var result = await new JsonPolicyProvider(path).LoadAsync(); Assert.False(result.IsValid); Assert.Contains(result.Issues, issue => issue.Field == "schemaVersion"); }
    [Fact] public void Destructive_default_conflict_policy_is_invalid() { var issues = JsonPolicyProvider.Validate(PolicyProfile.Conservative with { DefaultConflictPolicy = ConflictPolicy.Replace }); Assert.Contains(issues, issue => issue.Field == "defaultConflictPolicy"); }
    [Fact] public async Task Journal_round_trips_and_finds_incomplete_session() { var journal = new JsonMigrationJournal(root, NullLogger<JsonMigrationJournal>.Instance); var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var session = new MigrationSession(id, MigrationRole.OldPc, MigrationStatus.Interrupted, now, now); await journal.SaveAsync(session); Assert.Equal(session, await journal.LoadAsync(id)); var found = new List<MigrationSession>(); await foreach (var item in journal.FindIncompleteAsync()) found.Add(item); Assert.Contains(session, found); Assert.Empty(Directory.EnumerateFiles(root, "*.tmp")); }
    [Fact] public async Task Corrupt_journal_is_rejected() { var id = Guid.NewGuid(); await File.WriteAllTextAsync(Path.Combine(root, $"{id:N}.json"), "not-json"); var journal = new JsonMigrationJournal(root, NullLogger<JsonMigrationJournal>.Instance); await Assert.ThrowsAsync<InvalidDataException>(() => journal.LoadAsync(id)); }
    public void Dispose() => Directory.Delete(root, true);
}
