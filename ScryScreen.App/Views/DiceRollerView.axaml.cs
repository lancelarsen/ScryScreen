using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    private DiceRollerViewModel? _vm;

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _vm = DataContext as DiceRollerViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
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
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = e;
    }

    private async void OnPresetGearClick(object? sender, RoutedEventArgs e)
    {
        _ = e;

        if (_vm is null)
        {
            return;
        }

        if (sender is not Button btn)
        {
            return;
        }

        if (btn.DataContext is not DiceRollPresetViewModel preset)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;

        var dlg = new DiceRollPresetConfigWindow(preset)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        dlg.DeleteRequested += (_, __) =>
        {
            if (_vm.DeleteCustomRollPresetCommand.CanExecute(preset))
            {
                _vm.DeleteCustomRollPresetCommand.Execute(preset);
            }
        };

        if (owner is not null)
        {
            await dlg.ShowDialog(owner);
        }
        else
        {
            dlg.Show();
        }
    }
}
