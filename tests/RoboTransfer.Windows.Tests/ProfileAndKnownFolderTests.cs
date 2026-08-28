using RoboTransfer.Core;
using RoboTransfer.Windows;
using Xunit;
namespace RoboTransfer.Windows.Tests;
public sealed class ProfileAndKnownFolderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"RoboTransfer-{Guid.NewGuid():N}");
    public ProfileAndKnownFolderTests() => Directory.CreateDirectory(root);
    [Fact] public void Registered_domain_sid_with_existing_path_is_interactive() => Assert.Equal(ProfileClassification.InteractiveUser, WindowsUserProfileDetector.Classify("S-1-5-21-100-200-300-1001", root));
    [Theory][InlineData("S-1-5-18")][InlineData("S-1-5-19")][InlineData("S-1-5-20")] public void Service_profiles_are_excluded(string sid) => Assert.Equal(ProfileClassification.Service, WindowsUserProfileDetector.Classify(sid, root));
    [Fact] public void Missing_profile_path_is_stale() => Assert.Equal(ProfileClassification.Stale, WindowsUserProfileDetector.Classify("S-1-5-21-1-2-3-1001", Path.Combine(root, "missing")));
    [Fact] public void Unloaded_hive_uses_explicit_conventional_resolution() { var folders = WindowsKnownFolderResolver.Resolve("S-1-5-21-test", root, false); Assert.All(folders, folder => Assert.Equal(KnownFolderResolution.ConventionalPath, folder.Resolution)); }
    [Fact] public void Missing_profile_has_unresolved_known_folders() { var folders = WindowsKnownFolderResolver.Resolve("S-1-5-21-test", Path.Combine(root, "missing"), false); Assert.All(folders, folder => { Assert.Equal(KnownFolderResolution.Unresolved, folder.Resolution); Assert.Null(folder.Path); }); }
    public void Dispose() => Directory.Delete(root, true);
}
