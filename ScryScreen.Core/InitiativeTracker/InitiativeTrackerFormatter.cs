using System;
using System.Linq;
using System.Text;

namespace ScryScreen.Core.InitiativeTracker;

public static class InitiativeTrackerFormatter
{
    public sealed record Options(
        bool ShowRound = true,
        bool ShowInitiativeValues = true,
        bool IncludeHidden = false,
        int MaxEntries = 12)
    {
        public Options() : this(true, true, false, 12)
        {
        }
    }

    public static string ToPortalText(InitiativeTrackerState state, Options? options = null)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        options ??= new Options();

        var sb = new StringBuilder();

        if (options.ShowRound)
        {
            sb.Append("Round ");
            sb.Append(Math.Max(1, state.Round));
            sb.AppendLine();
        }

        var entries = state.Entries ?? Array.Empty<InitiativeEntry>();
        if (!options.IncludeHidden)
        {
            entries = entries.Where(e => !e.IsHidden).ToArray();
        }

        if (entries.Length == 0)
        {
            sb.Append("No combatants");
            return sb.ToString();
        }

        var activeId = state.ActiveId;
        var max = options.MaxEntries <= 0 ? entries.Length : Math.Min(options.MaxEntries, entries.Length);

        for (var i = 0; i < max; i++)
        {
            var e = entries[i];
            var isActive = activeId.HasValue && e.Id == activeId.Value;

            sb.Append(isActive ? "▶ " : "  ");

            if (options.ShowInitiativeValues)
            {
                sb.Append(e.Initiative);
                sb.Append("  ");
            }

            sb.Append(e.Name);

            if (i < max - 1)
            {
                sb.AppendLine();
            }
        }

        if (max < entries.Length)
        {
            sb.AppendLine();
            sb.Append("…");
            sb.Append(entries.Length - max);
            sb.Append(" more");
        }

        return sb.ToString();
    }
}
