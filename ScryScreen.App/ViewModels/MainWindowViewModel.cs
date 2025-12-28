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
        Media = new MediaLibraryViewModel();

        _portalHost.PortalClosed += OnPortalClosed;

        // Start with one portal for convenience.
        AddPortal();
    }

    public ReadOnlyCollection<ScreenInfoViewModel> Screens => _screens;

    public ObservableCollection<PortalRowViewModel> Portals { get; }

    public MediaLibraryViewModel Media { get; }

    [ObservableProperty]
    private PortalRowViewModel? selectedPortal;

    partial void OnSelectedPortalChanged(PortalRowViewModel? value)
    {
        SendSelectedMediaToSelectedPortalCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddPortal()
    {
        var portalNumber = Portals.Count + 1;
        var defaultScreen = Screens.FirstOrDefault();

        _portalHost.CreatePortal(portalNumber, defaultScreen);

        var portalRow = new PortalRowViewModel(_portalHost, portalNumber, Screens)
        {
            SelectedScreen = defaultScreen,
        };

        portalRow.DeleteRequested += OnDeletePortalRequested;

        Portals.Add(portalRow);
        SelectedPortal = portalRow;

        SendSelectedMediaToSelectedPortalCommand.NotifyCanExecuteChanged();
    }

    public void ImportMediaFolder(string folderPath)
    {
        Media.ImportFolder(folderPath);
        SendSelectedMediaToSelectedPortalCommand.NotifyCanExecuteChanged();
    }

    private bool CanSendSelectedMediaToSelectedPortal()
        => SelectedPortal is not null && Media.SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanSendSelectedMediaToSelectedPortal))]
    private void SendSelectedMediaToSelectedPortal()
    {
        var portal = SelectedPortal;
        var item = Media.SelectedItem;
        if (portal is null || item is null)
        {
            return;
        }

        portal.CurrentAssignment = item.DisplayName;
        _portalHost.SetContentImage(portal.PortalNumber, item.FilePath, item.DisplayName);
        portal.IsVisible = true;
    }

    [RelayCommand]
    private Task IdentifyPortalsAsync()
    {
        return _portalHost.IdentifyAllAsync();
    }

    public void Shutdown()
    {
        _portalHost.CloseAll();
    }

    private void OnDeletePortalRequested(int portalNumber)
    {
        _portalHost.ClosePortal(portalNumber);
    }

    private void OnPortalClosed(int portalNumber)
    {
        var row = Portals.FirstOrDefault(p => p.PortalNumber == portalNumber);
        if (row is null)
        {
            return;
        }

        row.DeleteRequested -= OnDeletePortalRequested;
        Portals.Remove(row);

        if (ReferenceEquals(SelectedPortal, row))
        {
            SelectedPortal = Portals.FirstOrDefault();
        }
    }
}
