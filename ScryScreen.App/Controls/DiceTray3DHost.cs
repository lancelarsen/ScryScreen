using System;
using System.IO;
using System.Text.Json;
using System.Numerics;
using Avalonia;
using Avalonia.Platform;

namespace ScryScreen.App.Controls;

public sealed class DiceTray3DHost : WebView2Host
{
    public event EventHandler<int>? DieClicked;
    public event EventHandler<DieRotationChangedEventArgs>? DieRotationChanged;
    public event EventHandler<DieRollCompletedEventArgs>? DieRollCompleted;

    public sealed class DieRotationChangedEventArgs : EventArgs
    {
        public required int Sides { get; init; }
        public required Quaternion Rotation { get; init; }
    }

    public sealed class DieRollCompletedEventArgs : EventArgs
    {
        public required long RequestId { get; init; }
        public required int Sides { get; init; }
        public required int Value { get; init; }
    }

    public DiceTray3DHost()
    {
        WebMessageReceived += OnWebMessageReceived;
        Html = LoadHtml();
    }

    public void ShowPreviewDice()
    {
        PostWebMessage("{\"type\":\"preview\"}");
    }

    public void ClearAllDice()
    {
        PostWebMessage("{\"type\":\"clearAll\"}");
    }

    public void ShowRollResults(System.Collections.Generic.IReadOnlyList<(int Sides, int Value)> dice)
    {
        if (dice is null || dice.Count == 0)
        {
            ClearAllDice();
            return;
        }

        // Keep payload small.
        var take = Math.Min(20, dice.Count);
        var limited = new System.Collections.Generic.List<(int Sides, int Value)>(take);
        for (var i = 0; i < take; i++)
        {
            limited.Add(dice[i]);
        }

        var payload = new
        {
            type = "roll",
            dice = System.Linq.Enumerable.Select(limited, d => new { sides = d.Sides, value = d.Value }),
        };

        PostWebMessage(JsonSerializer.Serialize(payload));
    }

    public void RequestRandomRoll(long requestId, int sides)
    {
        RequestRandomRoll(requestId, sides, ScryScreen.App.Models.DiceRollDirection.Right);
    }

    public void RequestRandomRoll(long requestId, int sides, ScryScreen.App.Models.DiceRollDirection direction)
    {
        if (requestId <= 0)
        {
            return;
        }

        if (sides is < 2 or > 100)
        {
            return;
        }

        var payload = new
        {
            type = "rollRandom",
            requestId,
            sides,
            direction = direction.ToString().ToLowerInvariant(),
        };

        PostWebMessage(JsonSerializer.Serialize(payload));
    }

    public void SetDieRotation(int sides, Quaternion rotation)
    {
        if (sides is < 2 or > 100)
        {
            return;
        }

        var payload = new
        {
            type = "setRotation",
            sides,
            q = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W },
        };

        PostWebMessage(JsonSerializer.Serialize(payload));
    }

    private void OnWebMessageReceived(object? sender, string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (!doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                return;
            }

            var type = typeEl.GetString();
            if (string.Equals(type, "die", StringComparison.OrdinalIgnoreCase))
            {
                if (!doc.RootElement.TryGetProperty("sides", out var sidesEl))
                {
                    return;
                }

                var sides = sidesEl.GetInt32();
                if (sides is < 2 or > 100)
                {
                    return;
                }

                DieClicked?.Invoke(this, sides);
                return;
            }

            if (string.Equals(type, "rotate", StringComparison.OrdinalIgnoreCase))
            {
                if (!doc.RootElement.TryGetProperty("sides", out var sidesEl))
                {
                    return;
                }

                var sides = sidesEl.GetInt32();
                if (sides is < 2 or > 100)
                {
                    return;
                }

                if (!doc.RootElement.TryGetProperty("q", out var qEl) || qEl.ValueKind != JsonValueKind.Array || qEl.GetArrayLength() != 4)
                {
                    return;
                }

                var x = qEl[0].GetSingle();
                var y = qEl[1].GetSingle();
                var z = qEl[2].GetSingle();
                var w = qEl[3].GetSingle();
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(w))
                {
                    return;
                }

                var rot = new Quaternion(x, y, z, w);
                if (rot.LengthSquared() < 1e-6f)
                {
                    return;
                }

                rot = Quaternion.Normalize(rot);
                DieRotationChanged?.Invoke(this, new DieRotationChangedEventArgs { Sides = sides, Rotation = rot });
                return;
            }

            if (string.Equals(type, "rollResult", StringComparison.OrdinalIgnoreCase))
            {
                if (!doc.RootElement.TryGetProperty("requestId", out var requestIdEl))
                {
                    return;
                }

                var requestId = requestIdEl.GetInt64();
                if (requestId <= 0)
                {
                    return;
                }

                if (!doc.RootElement.TryGetProperty("sides", out var sidesEl) || !doc.RootElement.TryGetProperty("value", out var valueEl))
                {
                    return;
                }

                var sides = sidesEl.GetInt32();
                var value = valueEl.GetInt32();
                if (sides is < 2 or > 100)
                {
                    return;
                }

                if (value <= 0 || value > sides)
                {
                    return;
                }

                DieRollCompleted?.Invoke(this, new DieRollCompletedEventArgs { RequestId = requestId, Sides = sides, Value = value });
                return;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string LoadHtml()
    {
        try
        {
            var uri = new Uri("avares://ScryScreen.App/Assets/Html/dice-tray.html");
            using var s = AssetLoader.Open(uri);
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch
        {
            // Fallback: minimal placeholder.
            return "<!doctype html><html><body style='margin:0;background:#0A0F18;color:#cbd5e1;font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100%'>3D dice unavailable</body></html>";
        }
    }
}
