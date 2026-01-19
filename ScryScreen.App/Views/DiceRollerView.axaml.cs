using System;
using Avalonia.Controls;
using ScryScreen.App.Controls;
using ScryScreen.App.Utilities;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Views;

public partial class DiceRollerView : UserControl
{
    public DiceRollerView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private DiceTray3DHost? _tray;
    private DiceRollerViewModel? _vm;

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _tray = this.FindControl<DiceTray3DHost>("DiceTray");
        if (_tray is not null)
        {
            _tray.DieClicked += OnDieClicked;
            _tray.ShowPreviewDice();
        }

        _vm = DataContext as DiceRollerViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            SyncTrayToVm();
        }
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }

        if (_tray is not null)
        {
            _tray.DieClicked -= OnDieClicked;
            _tray = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }

        _vm = DataContext as DiceRollerViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        SyncTrayToVm();
    }

    private void OnDieClicked(object? sender, int sides)
    {
        if (DataContext is not DiceRollerViewModel vm)
        {
            return;
        }

        vm.Expression = $"1d{sides}";
        if (vm.RollCommand.CanExecute(null))
        {
            vm.RollCommand.Execute(null);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiceRollerViewModel.LastResultText) || e.PropertyName == nameof(DiceRollerViewModel.RollId))
        {
            SyncTrayToVm();
        }
    }

    private void SyncTrayToVm()
    {
        if (_tray is null)
        {
            return;
        }

        var vm = _vm ?? (DataContext as DiceRollerViewModel);
        if (vm is null)
        {
            _tray.ShowPreviewDice();
            return;
        }

        if (string.IsNullOrWhiteSpace(vm.LastResultText))
        {
            _tray.ShowPreviewDice();
            return;
        }

        var dice = DiceRollTextParser.ParseDice(vm.LastResultText);
        _tray.ShowRollResults(dice);
    }
}
