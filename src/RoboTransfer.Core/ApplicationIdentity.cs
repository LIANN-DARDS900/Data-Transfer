using System.Reflection;

namespace RoboTransfer.Core;

public static class ApplicationIdentity
{
    public const string ProductName = "RoboTransfer";
    public static string Version => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ApplicationIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown-development-build";
}

public sealed record ApplicationDataLayout(string Root, string Sessions, string Manifests, string Plans, string Verification, string Reports, string Diagnostics, string Policies)
{
    public static ApplicationDataLayout CreateDefault() => Create(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationIdentity.ProductName));
    public static ApplicationDataLayout Create(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var canonical = Path.GetFullPath(root);
        if (!Path.IsPathFullyQualified(canonical)) throw new InvalidDataException("Application state root must be fully qualified.");
        EnsureNoReparsePoints(canonical);
        Directory.CreateDirectory(canonical);
        return new(canonical, Child("Sessions"), Child("Manifests"), Child("Plans"), Child("Verification"), Child("Reports"), Child("Diagnostics"), Child("Policies"));

        string Child(string name)
        {
            var path = Path.GetFullPath(Path.Combine(canonical, name));
            if (!IsInside(canonical, path)) throw new InvalidDataException("Application state path escaped its controlled root.");
            Directory.CreateDirectory(path);
            EnsureNoReparsePoints(path);
            return path;
        }
    }

    public string GetSessionDirectory(Guid sessionId)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("A non-empty session identity is required.", nameof(sessionId));
        var path = Path.GetFullPath(Path.Combine(Sessions, sessionId.ToString("N")));
        if (!IsInside(Sessions, path)) throw new InvalidDataException("Session path escaped its controlled root.");
        Directory.CreateDirectory(path);
        EnsureNoReparsePoints(path);
        return path;
    }

    private static bool IsInside(string root, string path) => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void EnsureNoReparsePoints(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null && current.Exists)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Application state cannot traverse a reparse point.");
            current = current.Parent;
        }
    }
}
