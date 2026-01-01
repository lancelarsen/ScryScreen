using ScryScreen.App.Services;
using ScryScreen.App.ViewModels;
using Xunit;

namespace ScryScreen.Tests;

public class PortalContentRestorationPlannerTests
{
    [Fact]
    public void CreateIdle_UsesDefaults_AndCustomTitle()
    {
        var plan = PortalContentRestorationPlanner.CreateIdle(idleTitle: "Waiting");

        Assert.Equal(PortalContentRestorationPlanner.ContentKind.Text, plan.Kind);
        Assert.True(plan.IsVisible);
        Assert.Equal("Waiting", plan.CurrentAssignment);
        Assert.Null(plan.AssignedMediaFilePath);
        Assert.False(plan.IsVideoLoop);
        Assert.Equal(MediaScaleMode.FillHeight, plan.ScaleMode);
        Assert.Equal(MediaAlign.Center, plan.Align);
    }

    [Fact]
    public void NullSnapshot_RestoresIdleText()
    {
        var plan = PortalContentRestorationPlanner.ComputeFromSnapshot(snapshot: null, idleTitle: "Idle");

        Assert.Equal(PortalContentRestorationPlanner.ContentKind.Text, plan.Kind);
        Assert.Equal("Idle", plan.CurrentAssignment);
        Assert.Null(plan.AssignedMediaFilePath);
        Assert.False(plan.IsVideoLoop);
    }

    [Fact]
    public void SnapshotWithoutFile_RestoresTextAssignment()
    {
        var snapshot = new PortalContentRestorationPlanner.Snapshot(
            IsVisible: false,
            CurrentAssignment: "Some Text",
            AssignedMediaFilePath: null,
            IsVideo: false,
            IsVideoLoop: true,
            ScaleMode: MediaScaleMode.FillWidth,
            Align: MediaAlign.End);

        var plan = PortalContentRestorationPlanner.ComputeFromSnapshot(snapshot, idleTitle: "Idle");

        Assert.Equal(PortalContentRestorationPlanner.ContentKind.Text, plan.Kind);
        Assert.Equal("Some Text", plan.CurrentAssignment);
        Assert.Null(plan.AssignedMediaFilePath);
        Assert.False(plan.IsVideoLoop);
        Assert.Equal(MediaScaleMode.FillWidth, plan.ScaleMode);
        Assert.Equal(MediaAlign.End, plan.Align);
        Assert.False(plan.IsVisible);
    }

    [Fact]
    public void SnapshotWithWhitespaceFilePath_TreatedAsNoFile()
    {
        var snapshot = new PortalContentRestorationPlanner.Snapshot(
            IsVisible: true,
            CurrentAssignment: "Some Text",
            AssignedMediaFilePath: "   ",
            IsVideo: true,
            IsVideoLoop: true,
            ScaleMode: MediaScaleMode.FillWidth,
            Align: MediaAlign.Start);

        var plan = PortalContentRestorationPlanner.ComputeFromSnapshot(snapshot, idleTitle: "Idle");

        Assert.Equal(PortalContentRestorationPlanner.ContentKind.Text, plan.Kind);
        Assert.Equal("Some Text", plan.CurrentAssignment);
        Assert.Null(plan.AssignedMediaFilePath);
        Assert.False(plan.IsVideoLoop);
        Assert.Equal(MediaScaleMode.FillWidth, plan.ScaleMode);
        Assert.Equal(MediaAlign.Start, plan.Align);
    }

    [Fact]
    public void SnapshotWithImage_RestoresImage()
    {
        var snapshot = new PortalContentRestorationPlanner.Snapshot(
            IsVisible: true,
            CurrentAssignment: "Map",
            AssignedMediaFilePath: "c:/map.png",
            IsVideo: false,
            IsVideoLoop: true,
            ScaleMode: MediaScaleMode.FillHeight,
            Align: MediaAlign.Center);

        var plan = PortalContentRestorationPlanner.ComputeFromSnapshot(snapshot, idleTitle: "Idle");

        Assert.Equal(PortalContentRestorationPlanner.ContentKind.Image, plan.Kind);
        Assert.Equal("c:/map.png", plan.AssignedMediaFilePath);
        Assert.False(plan.IsVideoLoop);
    }

    [Fact]
    public void SnapshotWithVideo_RestoresVideoAndLoopFlag()
    {
        var snapshot = new PortalContentRestorationPlanner.Snapshot(
            IsVisible: true,
            CurrentAssignment: "Clip",
            AssignedMediaFilePath: "c:/clip.mp4",
            IsVideo: true,
            IsVideoLoop: true,
            ScaleMode: MediaScaleMode.FillHeight,
            Align: MediaAlign.Center);

        var plan = PortalContentRestorationPlanner.ComputeFromSnapshot(snapshot, idleTitle: "Idle");

        Assert.Equal(PortalContentRestorationPlanner.ContentKind.Video, plan.Kind);
        Assert.Equal("c:/clip.mp4", plan.AssignedMediaFilePath);
        Assert.True(plan.IsVideoLoop);
    }
}
