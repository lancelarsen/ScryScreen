using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using ScryScreen.App.Controls;
using ScryScreen.App.ViewModels;
using ScryScreen.App.Services;
using ScryScreen.App.Views;

namespace ScryScreen.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var mainWindow = new MainWindow();
            ErrorReporter.Initialize(mainWindow);

            // Start WebView2 environment early to reduce first-use flashing when Dice Tray appears.
            WebView2Host.WarmUpWebView2();

            var portalHost = new PortalHostService(mainWindow);
            var vm = new MainWindowViewModel(portalHost);
            mainWindow.DataContext = vm;

            // Restore last saved session state (initiative/effects/media folder).
            LastSessionPersistence.Load(vm);
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.Activate();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}