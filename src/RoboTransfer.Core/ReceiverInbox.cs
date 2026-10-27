using System.Text.Json.Serialization;
namespace RoboTransfer.Core;

public sealed record ReceiverOffer(int SchemaVersion, Guid OfferId, string ApprovedRoot, string ReceiverMachine,
    string ReceiverProfileId, string ReceiverName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
{
    [JsonIgnore] public string InboxPath => ApprovedRoot + "\\RoboTransferInbox\\" + OfferId.ToString("N");
    [JsonIgnore] public string FilesPath => InboxPath + "\\Files";
    [JsonIgnore] public string Code => OfferId.ToString("N")[..8].ToUpperInvariant();
}
public sealed record ReceiverBinding(string InboxPath, Guid OfferId, string ReceiverMachine, string ReceiverName, string OfferFingerprint);
public sealed record ReceiverClaim(Guid OfferId, Guid SessionId, string SenderMachine, DateTimeOffset CreatedAt);
public enum ReceiverPhase { Ready, Transferring, Interrupted, CopyComplete, Verifying, Verified, Warnings, VerificationFailed, Cancelled, Failed, ReportReady }
public sealed record ReceiverStatus(int SchemaVersion, Guid OfferId, Guid SessionId, string SenderMachine,
    ReceiverPhase Phase, long Files, long Bytes, DateTimeOffset UpdatedAt);

// Implementations expose the current endpoint's identity, free space and filesystem access.
// Tests map a virtual approved UNC share into an isolated temporary directory.
public interface IReceiverEnvironment
{
    string MachineName { get; }
    bool IsLocalHost(string host);
    string GetAccessPath(string validatedUncPath);
    long? GetAvailableBytes(string validatedUncPath);
}

public static class ReceiverInboxPaths
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1000 || !path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains('/'))
            throw new InvalidDataException("Use an absolute approved Windows share path.");
        var parts = path.TrimEnd('\\').Substring(2).Split('\\');
        if (parts.Length < 2 || parts.Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c))))
            throw new InvalidDataException("The share path contains an unsupported component.");
        return @"\\" + string.Join('\\', parts);
    }
    public static string Host(string path) => Normalize(path).Substring(2).Split('\\')[0];
    public static string RequireInbox(PolicyProfile policy, string path)
    {
        var inbox = Normalize(path);
        if (!policy.AllowConfiguredNetworkShare) throw new InvalidOperationException("Network transfer is disabled by the current policy.");
        foreach (var allowed in policy.ApprovedNetworkSharePaths)
        {
            var root = Normalize(allowed);
            var prefix = root + "\\RoboTransferInbox\\";
            if (inbox.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(inbox[prefix.Length..], "N", out var id) && id != Guid.Empty)
                return root;
        }
        throw new InvalidOperationException("The receiver inbox must be inside a share explicitly listed in policy.");
    }
}
