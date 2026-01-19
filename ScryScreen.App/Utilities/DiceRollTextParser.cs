using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ScryScreen.App.Utilities;

public static class DiceRollTextParser
{
    private static readonly Regex DiceTermRegex = new(
        @"(?<count>\d+)d(?<sides>\d+)\((?<rolls>[^\)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static List<(int Sides, int Value)> ParseDice(string text)
    {
        var result = new List<(int Sides, int Value)>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (Match m in DiceTermRegex.Matches(text))
        {
            if (!m.Success)
            {
                continue;
            }

            if (!int.TryParse(m.Groups["sides"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sides) || sides <= 1)
            {
                continue;
            }

            var rolls = m.Groups["rolls"].Value;
            if (string.IsNullOrWhiteSpace(rolls))
            {
                continue;
            }

            var parts = rolls.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                {
                    result.Add((sides, v));
                }
            }
        }

        return result;
    }
}
