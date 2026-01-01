using ScryScreen.Core.Utilities;
using Xunit;

namespace ScryScreen.Tests;

public class PortalSnapshotRulesTests
{
    [Fact]
    public void ShouldApplyVideoSnapshot_False_WhenNotVideo()
    {
        Assert.False(PortalSnapshotRules.ShouldApplyVideoSnapshot("c:/a.mp4", isVideoAssigned: false, expectedFilePath: "c:/a.mp4"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldApplyVideoSnapshot_False_WhenAssignedMissing(string? assigned)
    {
        Assert.False(PortalSnapshotRules.ShouldApplyVideoSnapshot(assigned, isVideoAssigned: true, expectedFilePath: "c:/a.mp4"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldApplyVideoSnapshot_False_WhenExpectedMissing(string? expected)
    {
        Assert.False(PortalSnapshotRules.ShouldApplyVideoSnapshot("c:/a.mp4", isVideoAssigned: true, expectedFilePath: expected!));
    }

    [Fact]
    public void ShouldApplyVideoSnapshot_MatchesIgnoringCaseAndWhitespace()
    {
        Assert.True(PortalSnapshotRules.ShouldApplyVideoSnapshot(
            assignedMediaFilePath: " C:/VIDEO.MP4 ",
            isVideoAssigned: true,
            expectedFilePath: "c:/video.mp4"));

        Assert.True(PortalSnapshotRules.ShouldApplyVideoSnapshot(
            assignedMediaFilePath: "\tC:/VIDEO.MP4\r\n",
            isVideoAssigned: true,
            expectedFilePath: "\n c:/video.mp4 \t"));

        Assert.False(PortalSnapshotRules.ShouldApplyVideoSnapshot(
            assignedMediaFilePath: "c:/a.mp4",
            isVideoAssigned: true,
            expectedFilePath: "c:/b.mp4"));
    }
}
