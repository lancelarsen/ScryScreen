using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;

namespace ScryScreen.App.Views;

public partial class ErrorDialog : Window
{
    private Exception? _exception;
    private string? _context;

    public ErrorDialog()
    {
        InitializeComponent();

        ContextText.Text = "An unexpected error occurred.";
        DetailsBox.Text = "(No details available)";

        CopyButton.Click += OnCopy;
        CloseButton.Click += (_, _) => Close();
        QuitButton.Click += OnQuit;

        Opened += (_, _) =>
        {
            try
            {
                DetailsBox.CaretIndex = 0;
                DetailsBox.SelectionStart = 0;
                DetailsBox.SelectionEnd = 0;
            }
            catch
            {
                // ignored
            }
        };
    }

    public ErrorDialog(Exception exception, string? context = null)
        : this()
    {
        _exception = exception;
        _context = context;

        ContextText.Text = string.IsNullOrWhiteSpace(_context)
            ? "An unexpected error occurred."
            : $"An unexpected error occurred: {_context}";

        DetailsBox.Text = BuildDetailsText(_exception, _context);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        try
        {
            var text = DetailsBox.Text ?? string.Empty;
            IClipboard? clipboard = null;

            // Avalonia 11: clipboard is exposed on TopLevel/Window.
            clipboard ??= this.Clipboard;
            clipboard ??= TopLevel.GetTopLevel(this)?.Clipboard;

            if (clipboard is not null)
                await clipboard.SetTextAsync(text);
        }
        catch
        {
            // ignored
        }
    }

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        try
        {
            Close();
            Environment.Exit(1);
        }
        catch
        {
            // ignored
        }
    }

    private static string BuildDetailsText(Exception exception, string? context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Time: {DateTimeOffset.Now:O}");
        if (!string.IsNullOrWhiteSpace(context)) sb.AppendLine($"Context: {context}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Process: {Environment.ProcessPath}");
        sb.AppendLine();
        sb.AppendLine(exception.ToString());
        return sb.ToString();
    }
}
