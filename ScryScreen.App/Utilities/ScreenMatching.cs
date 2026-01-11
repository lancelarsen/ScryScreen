using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace ScryScreen.App.Utilities;

internal readonly record struct ScreenMatchCandidate(
    string PlatformDisplayName,
    PixelRect Bounds,
    double Scaling,
    bool IsPrimary)
{
    public int WidthPx => Bounds.Width;
    public int HeightPx => Bounds.Height;
}

internal static class ScreenMatching
{
    public static T? FindBestMatch<T>(ScreenMatchCandidate previous, IReadOnlyList<(ScreenMatchCandidate Candidate, T Value)> candidates)
        where T : class
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        static bool NearlyEqual(double a, double b, double epsilon = 0.01)
        {
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b))
            {
                return false;
            }

            return Math.Abs(a - b) <= epsilon;
        }

        // 1) Prefer matching the underlying platform display name when available.
        var prevPlatformName = previous.PlatformDisplayName;
        if (!string.IsNullOrWhiteSpace(prevPlatformName))
        {
            var byName = candidates
                .Where(x => string.Equals(x.Candidate.PlatformDisplayName, prevPlatformName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (byName.Count == 1)
            {
                return byName[0].Value;
            }

            if (byName.Count > 1)
            {
                var namePrimary = byName.FirstOrDefault(x => x.Candidate.IsPrimary == previous.IsPrimary);
                if (namePrimary.Value is not null)
                {
                    return namePrimary.Value;
                }
            }
        }

        // 2) Exact virtual-bounds match.
        var bounds = previous.Bounds;
        var byBounds = candidates.Where(x => x.Candidate.Bounds.Equals(bounds)).ToList();
        if (byBounds.Count == 1)
        {
            return byBounds[0].Value;
        }

        if (byBounds.Count > 1)
        {
            var boundsPrimary = byBounds.FirstOrDefault(x => x.Candidate.IsPrimary == previous.IsPrimary);
            if (boundsPrimary.Value is not null)
            {
                return boundsPrimary.Value;
            }

            var boundsScaling = byBounds.FirstOrDefault(x => NearlyEqual(x.Candidate.Scaling, previous.Scaling));
            if (boundsScaling.Value is not null)
            {
                return boundsScaling.Value;
            }
        }

        // 3) Resolution + scaling match.
        var byResolution = candidates
            .Where(x => x.Candidate.WidthPx == previous.WidthPx && x.Candidate.HeightPx == previous.HeightPx && NearlyEqual(x.Candidate.Scaling, previous.Scaling))
            .ToList();

        if (byResolution.Count == 1)
        {
            return byResolution[0].Value;
        }

        if (byResolution.Count > 1)
        {
            var resPrimary = byResolution.FirstOrDefault(x => x.Candidate.IsPrimary == previous.IsPrimary);
            if (resPrimary.Value is not null)
            {
                return resPrimary.Value;
            }
        }

        // 4) Nearest-by-position.
        var prevCx = bounds.X + (bounds.Width / 2.0);
        var prevCy = bounds.Y + (bounds.Height / 2.0);

        (ScreenMatchCandidate Candidate, T Value)? best = null;
        double bestScore = double.PositiveInfinity;

        foreach (var item in candidates)
        {
            var cb = item.Candidate.Bounds;
            var cx = cb.X + (cb.Width / 2.0);
            var cy = cb.Y + (cb.Height / 2.0);
            var dx = cx - prevCx;
            var dy = cy - prevCy;
            var dist2 = (dx * dx) + (dy * dy);

            var scalingPenalty = NearlyEqual(item.Candidate.Scaling, previous.Scaling) ? 0 : 1000;
            var score = dist2 + scalingPenalty;

            if (score < bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        // If the previous selection was primary, keep primary if it exists.
        if (previous.IsPrimary)
        {
            var primary = candidates.FirstOrDefault(x => x.Candidate.IsPrimary);
            if (primary.Value is not null)
            {
                return primary.Value;
            }
        }

        return best?.Value;
    }
}
