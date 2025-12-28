using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Services;

namespace ScryScreen.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly PortalHostService _portalHost;
    private readonly ReadOnlyCollection<ScreenInfoViewModel> _screens;

    public MainWindowViewModel(PortalHostService portalHost)
    {
        _portalHost = portalHost ?? throw new ArgumentNullException(nameof(portalHost));
        _screens = new ReadOnlyCollection<ScreenInfoViewModel>(_portalHost.GetScreens().ToList());
        Portals = new ObservableCollection<PortalRowViewModel>();

        // Start with one portal for convenience.
        AddPortal();
    }

    public ReadOnlyCollection<ScreenInfoViewModel> Screens => _screens;

    public ObservableCollection<PortalRowViewModel> Portals { get; }

    [ObservableProperty]
    private PortalRowViewModel? selectedPortal;

    [RelayCommand]
    private void AddPortal()
    {
        var portalNumber = Portals.Count + 1;
        var defaultScreen = Screens.FirstOrDefault();

        _portalHost.CreatePortal(portalNumber, defaultScreen);

        var portalRow = new PortalRowViewModel(_portalHost, portalNumber, Screens)
        {
            SelectedScreen = defaultScreen,
            IsVisible = false,
        };

        Portals.Add(portalRow);
        SelectedPortal = portalRow;
    }

    [RelayCommand]
    private Task IdentifyPortalsAsync()
    {
        return _portalHost.IdentifyAllAsync();
    }
}
