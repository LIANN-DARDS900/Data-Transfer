using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using RoboTransfer.Core;
namespace RoboTransfer.Windows;

public sealed class WindowsUserProfileDetector(ILogger<WindowsUserProfileDetector> logger) : IUserProfileDetector
{
    private const string ProfileList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    public Task<IReadOnlyList<UserProfile>> DetectAsync(CancellationToken cancellationToken = default) => Task.Run(() => Detect(cancellationToken), cancellationToken);

    private IReadOnlyList<UserProfile> Detect(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<UserProfile>();
        var profiles = new List<UserProfile>();
        using var root = Registry.LocalMachine.OpenSubKey(ProfileList);
        if (root is null) return profiles;
        foreach (var sid in root.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var profileKey = root.OpenSubKey(sid); var rawPath = profileKey?.GetValue("ProfileImagePath") as string;
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                var path = Environment.ExpandEnvironmentVariables(rawPath); var classification = Classify(sid, path);
                if (classification is ProfileClassification.Special or ProfileClassification.Service or ProfileClassification.Temporary or ProfileClassification.Stale) continue;
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                var hiveLoaded = IsHiveLoaded(sid);
                profiles.Add(new(sid, name, path, hiveLoaded, classification, classification == ProfileClassification.InteractiveUser && Directory.Exists(path), WindowsKnownFolderResolver.Resolve(sid, path, hiveLoaded)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { logger.LogWarning("A registered profile could not be inspected. ErrorCategory={ErrorCategory}; ExceptionType={ExceptionType}", ErrorCategory.AccessDenied, ex.GetType().Name); }
        }
        return profiles;
    }

    public static ProfileClassification Classify(string sid, string path)
    {
        if (sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20") return ProfileClassification.Service;
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (name.Equals("Public", StringComparison.OrdinalIgnoreCase) || name.Equals("Default", StringComparison.OrdinalIgnoreCase) || name.Equals("defaultuser0", StringComparison.OrdinalIgnoreCase)) return ProfileClassification.Special;
        if (name.StartsWith("TEMP", StringComparison.OrdinalIgnoreCase)) return ProfileClassification.Temporary;
        if (!Directory.Exists(path)) return ProfileClassification.Stale;
        return sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) ? ProfileClassification.InteractiveUser : ProfileClassification.Unknown;
    }

    private static bool IsHiveLoaded(string sid) { using var key = Registry.Users.OpenSubKey(sid); return key is not null; }
}

public static class WindowsKnownFolderResolver
{
    private const string ShellFolders = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private static readonly IReadOnlyDictionary<KnownFolderKind, string> Values = new Dictionary<KnownFolderKind, string>
    {
        [KnownFolderKind.Desktop] = "Desktop", [KnownFolderKind.Documents] = "Personal", [KnownFolderKind.Downloads] = "{374DE290-123F-4565-9164-39C4925E467B}", [KnownFolderKind.Pictures] = "My Pictures", [KnownFolderKind.Videos] = "My Video", [KnownFolderKind.Music] = "My Music", [KnownFolderKind.Favorites] = "Favorites"
    };
    public static IReadOnlyList<KnownFolder> Resolve(string sid, string profilePath, bool hiveLoaded)
    {
        RegistryKey? key = null;
        try { if (hiveLoaded) key = Registry.Users.OpenSubKey($@"{sid}\{ShellFolders}");
            return Values.Select(pair => ResolveOne(pair.Key, pair.Value, profilePath, key)).ToArray(); }
        finally { key?.Dispose(); }
    }
    private static KnownFolder ResolveOne(KnownFolderKind kind, string valueName, string profilePath, RegistryKey? key)
    {
        var configured = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = configured.Replace("%USERPROFILE%", profilePath, StringComparison.OrdinalIgnoreCase);
            if (path.Contains('%', StringComparison.Ordinal)) return new(kind, null, false, KnownFolderResolution.Unresolved, "The shell-folder mapping uses user-specific environment variables that cannot be resolved safely in the technician context.");
            if (!Path.IsPathFullyQualified(path)) return new(kind, null, false, KnownFolderResolution.Unresolved, "The registered shell-folder mapping is not an absolute path.");
            return new(kind, path, Directory.Exists(path), KnownFolderResolution.Resolved, "Resolved from the registered user shell-folder mapping.");
        }
        if (!Directory.Exists(profilePath)) return new(kind, null, false, KnownFolderResolution.Unresolved, "The registered profile directory is unavailable.");
        var conventional = Path.Combine(profilePath, kind.ToString());
        return new(kind, conventional, Directory.Exists(conventional), KnownFolderResolution.ConventionalPath, "No loaded shell-folder mapping was available; this conventional path requires technician confirmation.");
    }
}
