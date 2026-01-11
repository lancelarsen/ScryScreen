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
            // Keep SemVer without build metadata (e.g. strip "+<gitsha>").
            var shortInformational = informational.Split('+')[0];
            return shortInformational;
        }

        var version = asm.GetName().Version;
        if (version is null)
        {
            return "(unknown)";
        }

        // Prefer Major.Minor.Build (omit Revision).
        if (version.Build >= 0)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        return $"{version.Major}.{version.Minor}";
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
