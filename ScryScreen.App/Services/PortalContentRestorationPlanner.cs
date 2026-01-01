using System;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Services;

public static class PortalContentRestorationPlanner
{
    public enum ContentKind
    {
        Text,
        Image,
        Video,
    }

    public readonly record struct Snapshot(
        bool IsVisible,
        string CurrentAssignment,
        string? AssignedMediaFilePath,
        bool IsVideo,
        bool IsVideoLoop,
        MediaScaleMode ScaleMode,
        MediaAlign Align);

    public readonly record struct RestorePlan(
        ContentKind Kind,
        bool IsVisible,
        string CurrentAssignment,
        string? AssignedMediaFilePath,
        bool IsVideoLoop,
        MediaScaleMode ScaleMode,
        MediaAlign Align);

    public static RestorePlan CreateIdle(string idleTitle = "Idle")
    {
        return new RestorePlan(
            Kind: ContentKind.Text,
            IsVisible: true,
            CurrentAssignment: idleTitle,
            AssignedMediaFilePath: null,
            IsVideoLoop: false,
            ScaleMode: MediaScaleMode.FillHeight,
            Align: MediaAlign.Center);
    }

    public static RestorePlan ComputeFromSnapshot(Snapshot? snapshot, string idleTitle = "Idle")
    {
        if (snapshot is null)
        {
            return CreateIdle(idleTitle);
        }

        var s = snapshot.Value;

        if (string.IsNullOrWhiteSpace(s.AssignedMediaFilePath))
        {
            return new RestorePlan(
                Kind: ContentKind.Text,
                IsVisible: s.IsVisible,
                CurrentAssignment: s.CurrentAssignment,
                AssignedMediaFilePath: null,
                IsVideoLoop: false,
                ScaleMode: s.ScaleMode,
                Align: s.Align);
        }

        return new RestorePlan(
            Kind: s.IsVideo ? ContentKind.Video : ContentKind.Image,
            IsVisible: s.IsVisible,
            CurrentAssignment: s.CurrentAssignment,
            AssignedMediaFilePath: s.AssignedMediaFilePath,
            IsVideoLoop: s.IsVideo ? s.IsVideoLoop : false,
            ScaleMode: s.ScaleMode,
            Align: s.Align);
    }
}
