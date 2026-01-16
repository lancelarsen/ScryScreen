using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScryScreen.Core.Utilities;

public static class DiceExpressionEvaluator
{
    public sealed record DiceEvaluationResult(int Total, string DisplayText);

    private sealed record Term(bool IsNegative, int DiceCount, int DiceSides, int Constant);

    public static bool TryEvaluate(string? expression, Random rng, out DiceEvaluationResult result, out string? error)
    {
        result = new DiceEvaluationResult(0, string.Empty);
        error = null;

        if (rng is null)
        {
            error = "Random source is required.";
            return false;
        }

        var text = (expression ?? string.Empty).Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Expression is empty.";
            return false;
        }

        if (!TryParseTerms(text, out var terms, out error))
        {
            return false;
        }

        var total = 0;
        var sb = new StringBuilder();

        for (var i = 0; i < terms.Count; i++)
        {
            var term = terms[i];
            var sign = term.IsNegative ? -1 : 1;

            if (i > 0)
            {
                sb.Append(term.IsNegative ? " - " : " + ");
            }
            else if (term.IsNegative)
            {
                sb.Append("-");
            }

            if (term.DiceSides > 0)
            {
                sb.Append(term.DiceCount.ToString(CultureInfo.InvariantCulture));
                sb.Append('d');
                sb.Append(term.DiceSides.ToString(CultureInfo.InvariantCulture));
                sb.Append('(');

                var subtotal = 0;
                for (var r = 0; r < term.DiceCount; r++)
                {
                    var roll = rng.Next(1, term.DiceSides + 1);
                    subtotal += roll;
                    if (r > 0) sb.Append(',');
                    sb.Append(roll.ToString(CultureInfo.InvariantCulture));
                }

                sb.Append(')');
                total += sign * subtotal;
            }
            else
            {
                sb.Append(term.Constant.ToString(CultureInfo.InvariantCulture));
                total += sign * term.Constant;
            }
        }

        sb.Append(" = ");
        sb.Append(total.ToString(CultureInfo.InvariantCulture));

        result = new DiceEvaluationResult(total, sb.ToString());
        return true;
    }

    private static bool TryParseTerms(string text, out List<Term> terms, out string? error)
    {
        terms = new List<Term>();
        error = null;

        var index = 0;
        var isFirst = true;

        while (index < text.Length)
        {
            var isNegative = false;

            if (text[index] == '+')
            {
                index++;
            }
            else if (text[index] == '-')
            {
                isNegative = true;
                index++;
            }
            else if (!isFirst)
            {
                error = "Expected '+' or '-' between terms.";
                return false;
            }

            isFirst = false;

            if (index >= text.Length)
            {
                error = "Trailing operator.";
                return false;
            }

            // Find end of this term (next + or -)
            var start = index;
            while (index < text.Length && text[index] != '+' && text[index] != '-')
            {
                index++;
            }

            var termText = text.Substring(start, index - start);
            if (string.IsNullOrWhiteSpace(termText))
            {
                error = "Empty term.";
                return false;
            }

            if (TryParseDiceTerm(termText, out var diceCount, out var diceSides))
            {
                if (diceCount <= 0 || diceSides <= 0)
                {
                    error = "Dice term must be positive.";
                    return false;
                }

                if (diceCount > 1000)
                {
                    error = "Too many dice (max 1000).";
                    return false;
                }

                if (diceSides > 100000)
                {
                    error = "Dice sides too large (max 100000).";
                    return false;
                }

                terms.Add(new Term(isNegative, diceCount, diceSides, Constant: 0));
                continue;
            }

            if (!int.TryParse(termText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var constant))
            {
                error = $"Invalid term '{termText}'.";
                return false;
            }

            terms.Add(new Term(isNegative, DiceCount: 0, DiceSides: 0, Constant: constant));
        }

        if (terms.Count == 0)
        {
            error = "Expression has no terms.";
            return false;
        }

        return true;
    }

    private static bool TryParseDiceTerm(string termText, out int diceCount, out int diceSides)
    {
        diceCount = 0;
        diceSides = 0;

        var dIndex = termText.IndexOf('d');
        if (dIndex < 0)
        {
            dIndex = termText.IndexOf('D');
        }

        if (dIndex < 0)
        {
            return false;
        }

        var left = termText.Substring(0, dIndex);
        var right = termText.Substring(dIndex + 1);

        diceCount = 1;
        if (!string.IsNullOrWhiteSpace(left))
        {
            if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out diceCount))
            {
                return false;
            }
        }

        if (!int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out diceSides))
        {
            return false;
        }

        return true;
    }
}
