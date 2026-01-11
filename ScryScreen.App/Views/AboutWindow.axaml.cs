using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Reflection;
using ScryScreen.App.Utilities;

namespace ScryScreen.App.Views;

public partial class AboutWindow : Window
{
    private const string LanceSiteUrl = "https://lancelarsen.com";
    private const string GitHubRepoUrl = "https://github.com/lancelarsen/ScryScreen";

    public string VersionText { get; }

    public AboutWindow()
    {
        InitializeComponent();
        VersionText = BuildVersionText();
        DataContext = this;
    }

    private static string BuildVersionText()
    {
        var asm = typeof(AboutWindow).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return $"Version: {informational}";
        }

        var version = asm.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "Version: (unknown)" : $"Version: {version}";
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnOpenLanceSite(object? sender, RoutedEventArgs e)
    {
        UrlLauncher.TryOpen(LanceSiteUrl);
    }

    private void OnOpenGitHub(object? sender, RoutedEventArgs e)
    {
        UrlLauncher.TryOpen(GitHubRepoUrl);
    }
}
