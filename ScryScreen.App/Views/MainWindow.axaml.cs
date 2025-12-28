using Avalonia.Controls;
using System.ComponentModel;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Shutdown();
        }
    }
}