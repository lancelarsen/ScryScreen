using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Services;

namespace ScryScreen.App.ViewModels;

public partial class PortalRowViewModel : ViewModelBase
{
    private readonly PortalHostService _portalHost;

    public event Action<int>? DeleteRequested;

    public PortalRowViewModel(PortalHostService portalHost, int portalNumber, IReadOnlyList<ScreenInfoViewModel> availableScreens)
    {
        _portalHost = portalHost;
        PortalNumber = portalNumber;
        AvailableScreens = availableScreens;
        IsVisible = true;
        currentAssignment = "Idle";
    }

    public int PortalNumber { get; }

    public string Title => $"Portal {PortalNumber}";

    public IReadOnlyList<ScreenInfoViewModel> AvailableScreens { get; }

    [ObservableProperty]
    private ScreenInfoViewModel? selectedScreen;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string currentAssignment;

    partial void OnSelectedScreenChanged(ScreenInfoViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _portalHost.AssignToScreen(PortalNumber, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        _portalHost.SetVisibility(PortalNumber, value);
    }

    [RelayCommand]
    private void DeletePortal()
    {
        DeleteRequested?.Invoke(PortalNumber);
    }

    [RelayCommand]
    private void ClosePortal()
    {
        // Close = remove this portal window + row.
        DeleteRequested?.Invoke(PortalNumber);
    }
}
