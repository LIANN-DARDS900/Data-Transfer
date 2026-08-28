using RoboTransfer.Core;
using RoboTransfer.Windows;
using Xunit;
namespace RoboTransfer.Windows.Tests;
public sealed class CloudPlaceholderTests
{
    [Fact] public void Recall_on_data_access_is_online_only() => Assert.Equal(CloudContentState.OnlineOnly, WindowsCloudPlaceholderDetector.Classify((FileAttributes)0x00400000));
    [Fact] public void Pinned_non_offline_content_is_pinned() => Assert.Equal(CloudContentState.Pinned, WindowsCloudPlaceholderDetector.Classify((FileAttributes)0x00080000));
    [Fact] public void Offline_content_is_partially_available() => Assert.Equal(CloudContentState.PartiallyAvailable, WindowsCloudPlaceholderDetector.Classify(FileAttributes.Offline));
    [Fact] public void Ordinary_content_is_locally_available() => Assert.Equal(CloudContentState.LocallyAvailable, WindowsCloudPlaceholderDetector.Classify(FileAttributes.Archive));
}
