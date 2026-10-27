using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoboTransfer.Core;
namespace RoboTransfer.Persistence;

public sealed class ReceiverInboxService(IReceiverEnvironment environment, TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    public string MachineName => environment.MachineName;
    public bool IsHostedHere(string root) => environment.IsLocalHost(ReceiverInboxPaths.Host(root));
    public string GetFilesAccessPath(PolicyProfile policy, ReceiverBinding binding)
    {
        _ = ReceiverInboxPaths.RequireInbox(policy, binding.InboxPath);
        var files = Path.Combine(Access(binding.InboxPath), "Files");
        CheckPath(files);
        return files;
    }

    public async Task<ReceiverOffer> PrepareAsync(PolicyProfile policy, string root, UserProfile profile, CancellationToken token = default)
    {
        root = ReceiverInboxPaths.Normalize(root);
        if (!policy.AllowConfiguredNetworkShare || !policy.ApprovedNetworkSharePaths.Any(p => string.Equals(ReceiverInboxPaths.Normalize(p), root, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an existing share explicitly approved by policy.");
        if (!profile.IsSelectable) throw new InvalidOperationException("Choose a registered employee profile.");
        if (!environment.IsLocalHost(ReceiverInboxPaths.Host(root)))
            throw new InvalidOperationException("For PC-to-PC receiving, the approved share must be hosted by this new PC.");
        var accessRoot = environment.GetAccessPath(root);
        if (!Directory.Exists(accessRoot)) throw new IOException("The approved share is unreachable. Check Ethernet, Windows permissions and the approved network route.");
        CheckPath(accessRoot);
        var now = clock.GetUtcNow();
        var offer = new ReceiverOffer(1, Guid.NewGuid(), root, environment.MachineName, profile.Id, profile.DisplayName, now, now.AddHours(12));
        var inbox = Access(offer.InboxPath);
        Directory.CreateDirectory(inbox); CheckPath(inbox);
        Directory.CreateDirectory(Path.Combine(inbox, "Files"));
        await WriteAsync(inbox, "receiver.json", offer, false, token);
        return offer;
    }

    public async Task<ReceiverOffer> ReadOfferAsync(PolicyProfile policy, string path, CancellationToken token = default, bool allowExpired = false)
    {
        path = ReceiverInboxPaths.Normalize(path);
        var root = ReceiverInboxPaths.RequireInbox(policy, path); // Authorize before any filesystem/network access.
        var inbox = Access(path);
        var offer = await ReadAsync<ReceiverOffer>(inbox, "receiver.json", token) ?? throw new IOException("Receiver details were not found. Prepare an inbox on the new PC first.");
        if (offer.SchemaVersion != 1 || offer.OfferId == Guid.Empty || !Same(offer.ApprovedRoot, root) || !Same(offer.InboxPath, path) ||
            !IsSafeLabel(offer.ReceiverMachine) || !IsSafeLabel(offer.ReceiverProfileId) || !IsSafeLabel(offer.ReceiverName) ||
            offer.CreatedAt > clock.GetUtcNow().AddMinutes(5) || offer.ExpiresAt <= offer.CreatedAt ||
            !string.Equals(ReceiverInboxPaths.Host(root).Split('.')[0], offer.ReceiverMachine, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Receiver details are invalid or inconsistent with the selected PC and share.");
        if (!allowExpired && offer.ExpiresAt <= clock.GetUtcNow()) throw new InvalidOperationException("This receiver inbox expired. Prepare a new inbox.");
        if (File.Exists(Path.Combine(inbox, "closed.json"))) throw new InvalidOperationException("The receiver closed this inbox. Prepare a new inbox.");
        CheckPath(Path.Combine(inbox, "Files"));
        return offer;
    }

    public static ReceiverBinding Bind(ReceiverOffer offer) => new(offer.InboxPath, offer.OfferId, offer.ReceiverMachine, offer.ReceiverName, Fingerprint(offer));

    public async Task ValidateAsync(PolicyProfile policy, ReceiverBinding binding, Guid? sessionId, long requiredBytes, CancellationToken token)
    {
        var offer = await ReadOfferAsync(policy, binding.InboxPath, token, allowExpired: sessionId.HasValue);
        if (Bind(offer) != binding) throw new InvalidDataException("The receiver changed after confirmation. Check the recipient again.");
        var claim = await ReadAsync<ReceiverClaim>(Access(binding.InboxPath), "sender.json", token);
        if (claim is not null && (claim.OfferId != offer.OfferId || claim.SessionId != sessionId || !string.Equals(claim.SenderMachine, environment.MachineName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Another migration already owns this inbox. Prepare a separate inbox.");
        if (sessionId.HasValue && claim is null) throw new InvalidOperationException("The sender claim is missing. Prepare and confirm a new transfer.");
        var available = environment.GetAvailableBytes(offer.InboxPath);
        if (!available.HasValue) throw new IOException("Destination free space could not be checked. Reconnect the approved share.");
        if (available < requiredBytes) throw new IOException("There is not enough free space on the receiving PC.");
    }

    public async Task ClaimAsync(PolicyProfile policy, ReceiverBinding binding, Guid sessionId, long requiredBytes, CancellationToken token)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("A migration session is required.", nameof(sessionId));
        var offer = await ReadOfferAsync(policy, binding.InboxPath, token);
        if (Bind(offer) != binding) throw new InvalidDataException("Receiver details changed after review.");
        var inbox = Access(binding.InboxPath);
        var current = await ReadAsync<ReceiverClaim>(inbox, "sender.json", token);
        if (current is null)
        {
            await ValidateAsync(policy, binding, null, requiredBytes, token);
            try { await WriteAsync(inbox, "sender.json", new ReceiverClaim(offer.OfferId, sessionId, environment.MachineName, clock.GetUtcNow()), false, token); }
            catch (IOException) when (File.Exists(Path.Combine(inbox, "sender.json"))) { }
        }
        await ValidateAsync(policy, binding, sessionId, requiredBytes, token);
    }

    public async Task PublishAsync(PolicyProfile policy, ReceiverBinding binding, Guid sessionId, ReceiverPhase phase, long files, long bytes, CancellationToken token = default)
    {
        if (files < 0 || bytes < 0 || !Enum.IsDefined(phase)) throw new ArgumentException("Invalid receiver progress.");
        await ValidateAsync(policy, binding, sessionId, 0, token);
        await WriteAsync(Access(binding.InboxPath), "status.json", new ReceiverStatus(1, binding.OfferId, sessionId, environment.MachineName, phase, files, bytes, clock.GetUtcNow()), true, token);
    }

    public async Task<ReceiverStatus?> ReadStatusAsync(PolicyProfile policy, ReceiverOffer expected, CancellationToken token = default)
    {
        var offer = await ReadOfferAsync(policy, expected.InboxPath, token, allowExpired: true);
        if (Bind(expected) != Bind(offer)) throw new InvalidDataException("Receiver details changed. Stop and inspect this inbox.");
        var inbox = Access(offer.InboxPath);
        var status = await ReadAsync<ReceiverStatus>(inbox, "status.json", token);
        if (status is null) return null;
        var claim = await ReadAsync<ReceiverClaim>(inbox, "sender.json", token);
        if (status.SchemaVersion != 1 || !Enum.IsDefined(status.Phase) || status.OfferId != offer.OfferId || status.SessionId == Guid.Empty ||
            claim is null || claim.OfferId != offer.OfferId || claim.SessionId != status.SessionId || claim.SenderMachine != status.SenderMachine ||
            status.Files < 0 || status.Bytes < 0 || status.UpdatedAt > clock.GetUtcNow().AddMinutes(5))
            throw new InvalidDataException("Sender status is invalid. It cannot be used as migration evidence.");
        return status;
    }

    public async Task CloseAsync(PolicyProfile policy, ReceiverOffer offer, CancellationToken token = default)
    {
        _ = await ReadOfferAsync(policy, offer.InboxPath, token, allowExpired: true);
        var inbox = Access(offer.InboxPath);
        // Once claimed, leave the inbox available for validated resume. No remotely induced cancellation.
        if (File.Exists(Path.Combine(inbox, "sender.json"))) throw new InvalidOperationException("This inbox belongs to a migration. Keep it available for completion or recovery.");
        await WriteAsync(inbox, "closed.json", new { offer.OfferId, ClosedAt = clock.GetUtcNow() }, false, token);
    }

    public async Task PublishReportsAsync(PolicyProfile policy, ReceiverBinding binding, Guid sessionId, string jsonPath, string htmlPath, CancellationToken token)
    {
        await ValidateAsync(policy, binding, sessionId, 0, token);
        var inbox = Access(binding.InboxPath);
        foreach (var (source, name) in new[] { (jsonPath, "report.json"), (htmlPath, "report.html") })
        {
            var data = await File.ReadAllBytesAsync(source, token);
            if (data.Length > 10 * 1024 * 1024) throw new InvalidDataException("Report exceeds the inbox publication limit.");
            await WriteBytesAsync(inbox, name, data, true, token);
        }
    }

    private string Access(string path)
    {
        var access = environment.GetAccessPath(path);
        CheckPath(access);
        return access;
    }
    private static bool IsSafeLabel(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private static bool Same(string a, string b) => string.Equals(ReceiverInboxPaths.Normalize(a), ReceiverInboxPaths.Normalize(b), StringComparison.OrdinalIgnoreCase);
    private static string Fingerprint(ReceiverOffer offer) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(offer, Json)));
    private static async Task<T?> ReadAsync<T>(string inbox, string name, CancellationToken token) where T : class
    {
        var path = Path.Combine(inbox, name); CheckPath(path);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
        if (stream.Length > 65536) throw new InvalidDataException("Inbox metadata exceeds the supported size.");
        var buffer = new byte[65537];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(length), token);
            if (count == 0) break;
            length += count;
        }
        if (length > 65536) throw new InvalidDataException("Inbox metadata exceeds the supported size.");
        try { return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), Json) ?? throw new InvalidDataException("Inbox metadata is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Inbox metadata is malformed or unsupported.", ex); }
    }
    private static Task WriteAsync<T>(string inbox, string name, T value, bool replace, CancellationToken token) => WriteBytesAsync(inbox, name, JsonSerializer.SerializeToUtf8Bytes(value, Json), replace, token);
    private static async Task WriteBytesAsync(string inbox, string name, byte[] data, bool replace, CancellationToken token)
    {
        CheckPath(inbox);
        var target = Path.Combine(inbox, name); CheckPath(target);
        var temporary = Path.Combine(inbox, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(data, token); await stream.FlushAsync(token); }
            CheckPath(inbox); CheckPath(target);
            File.Move(temporary, target, replace);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void CheckPath(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Receiver inbox paths cannot use reparse points.");
    }
}
