using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ScryScreen.App.Controls;

public sealed class BoldTotalsTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> RawTextProperty =
        AvaloniaProperty.Register<BoldTotalsTextBlock, string?>(nameof(RawText));

    private static readonly Regex TotalRegex = new(@"=\s*(-?\d+)", RegexOptions.Compiled);

    public string? RawText
    {
        get => GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    static BoldTotalsTextBlock()
    {
        RawTextProperty.Changed.AddClassHandler<BoldTotalsTextBlock>((x, _) => x.RebuildInlines());
    }

    private void RebuildInlines()
    {
        var inlines = Inlines;
        if (inlines is null)
        {
            return;
        }

        inlines.Clear();

        var text = RawText ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var matches = TotalRegex.Matches(text);
        if (matches.Count == 0)
        {
            inlines.Add(new Run(text));
            return;
        }

        var cursor = 0;
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var numberGroup = match.Groups[1];
            if (!numberGroup.Success)
            {
                continue;
            }

            if (numberGroup.Index > cursor)
            {
                inlines.Add(new Run(text.Substring(cursor, numberGroup.Index - cursor)));
            }

            inlines.Add(new Run(numberGroup.Value)
            {
                FontWeight = FontWeight.Bold,
            });

            cursor = numberGroup.Index + numberGroup.Length;
        }

        if (cursor < text.Length)
        {
            inlines.Add(new Run(text.Substring(cursor)));
        }
    }
}
