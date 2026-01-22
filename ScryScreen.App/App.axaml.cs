using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

            // Startup warmup: programmatically "click" Dice Tray and then Portal 1.
            // This forces WebView2 + tray HTML/JS to initialize early.
            mainWindow.Opened += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        vm.SelectDiceRollerAppCommand.Execute(null);

                        var portal1 = vm.Portals.FirstOrDefault(p => p.PortalNumber == 1);
                        if (portal1 is not null && !portal1.IsSelectedForDiceRoller)
                        {
                            vm.ToggleDiceRollerForPortalCommand.Execute(portal1);
                        }

                        // Give WebView2/tray a moment to spin up, then revert the UI back
                        // to the normal Images tab and unselect Portal 1 for Dice Tray.
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    if (portal1 is not null && portal1.IsSelectedForDiceRoller)
                                    {
                                        vm.ToggleDiceRollerForPortalCommand.Execute(portal1);
                                    }

                                    vm.ShowImagesTabCommand.Execute(null);
                                }
                                catch (System.Exception ex)
                                {
                                    ErrorReporter.Report(ex, "Startup Dice Tray warmup (restore)");
                                }
                            }, DispatcherPriority.Background);
                        });
                    }
                    catch (System.Exception ex)
                    {
                        ErrorReporter.Report(ex, "Startup Dice Tray warmup");
                    }
                }, DispatcherPriority.Background);
            };

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