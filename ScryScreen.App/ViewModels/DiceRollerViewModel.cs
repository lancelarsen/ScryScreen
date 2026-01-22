using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;
using ScryScreen.App.Services;
using ScryScreen.Core.Utilities;

namespace ScryScreen.App.ViewModels;

public partial class DiceRollerViewModel : ViewModelBase
{
    private readonly Random _rng = new();
    private long _rollIdCounter;
    private long _clearDiceIdCounter;
    private readonly Dictionary<int, DiceDieRotation> _rotationsBySides = new();

    private const int MaxTrayDiceCount = 20;

    private static double Clamp(double value, double min, double max)
        => value < min ? min : (value > max ? max : value);

    private sealed class PendingTrayRollTerm
    {
        public required int Sides { get; init; }
        public required int Count { get; init; }
        public required int Sign { get; init; }
        public required List<int> Values { get; init; }
    }

    private sealed class PendingTrayRoll
    {
        public required DiceRollRequest Request { get; init; }
        public required List<PendingTrayRollTerm> Terms { get; init; }
        public required int ConstantTotal { get; init; }
    }

    private readonly Dictionary<long, PendingTrayRoll> _pendingTrayRollsByRequestId = new();
    private DiceRollRequest? _lastIssuedRollRequest;

    private long _visualConfigRevision;

    public event Action? StateChanged;

    public DiceRollerViewModel()
    {
        Expression = "1d20";

        DiceVisualConfigs = new ObservableCollection<DiceDieVisualConfigViewModel>(new[]
        {
            new DiceDieVisualConfigViewModel(4, 1.0, 1.0, NotifyDiceVisualConfigChanged),
            new DiceDieVisualConfigViewModel(6, 1.0, 1.0, NotifyDiceVisualConfigChanged),
            new DiceDieVisualConfigViewModel(8, 1.0, 1.0, NotifyDiceVisualConfigChanged),
            new DiceDieVisualConfigViewModel(10, 1.0, 1.0, NotifyDiceVisualConfigChanged),
            new DiceDieVisualConfigViewModel(12, 1.0, 1.0, NotifyDiceVisualConfigChanged),
            new DiceDieVisualConfigViewModel(20, 1.0, 1.0, NotifyDiceVisualConfigChanged),
            new DiceDieVisualConfigViewModel(100, 1.0, 1.0, NotifyDiceVisualConfigChanged),
        });

        DiceRollerEventBus.SingleDieRollCompleted += OnSingleDieRollCompleted;
    }

    public ObservableCollection<string> History { get; } = new();

    public ObservableCollection<DiceDieVisualConfigViewModel> DiceVisualConfigs { get; }

    [ObservableProperty]
    private string expression = "1d20";

    [ObservableProperty]
    private string lastResultText = string.Empty;

    [ObservableProperty]
    private string? lastErrorText;

    [ObservableProperty]
    private bool showDiceConfiguration;

    [ObservableProperty]
    private bool showDebugInfo;

    [ObservableProperty]
    private DiceRollDirection rollDirection = DiceRollDirection.Right;

    [ObservableProperty]
    private long rollId;

    public DiceRollerState SnapshotState()
    {
        // Keep the portal overlay alive so dice can appear immediately when requested.
        var text = string.IsNullOrWhiteSpace(LastResultText) ? "Dice Tray" : LastResultText;
        var rotations = _rotationsBySides.Count == 0
            ? Array.Empty<DiceDieRotation>()
            : _rotationsBySides.Values.OrderBy(r => r.Sides).ToArray();

        var visualConfigs = DiceVisualConfigs.Count == 0
            ? Array.Empty<DiceDieVisualConfig>()
            : DiceVisualConfigs
                .Select(c =>
                {
                    var dieScale = Clamp(c.DieScale, 0.5, 1.75);
                    var numberScale = Clamp(c.NumberScale, 0.5, 2.0);
                    return new DiceDieVisualConfig(c.Sides, dieScale, numberScale);
                })
                .OrderBy(c => c.Sides)
                .ToArray();

        return new(
            text,
            RollId,
            rotations,
            visualConfigs,
            VisualConfigRevision: _visualConfigRevision,
            RollDirection,
            _lastIssuedRollRequest,
            _clearDiceIdCounter,
            DebugVisible: ShowDebugInfo);
    }

    public bool IsRollDirectionRight => RollDirection == DiceRollDirection.Right;
    public bool IsRollDirectionLeft => RollDirection == DiceRollDirection.Left;
    public bool IsRollDirectionUp => RollDirection == DiceRollDirection.Up;
    public bool IsRollDirectionDown => RollDirection == DiceRollDirection.Down;
    public bool IsRollDirectionRandom => RollDirection == DiceRollDirection.Random;

    private static bool TryParseTrayExpression(string? expression, out List<DiceRollDiceTerm> terms, out int constantTotal, out string? error)
    {
        terms = new List<DiceRollDiceTerm>();
        constantTotal = 0;
        error = null;

        var text = (expression ?? string.Empty).Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int i = 0;
        var sign = +1;

        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '+')
            {
                sign = +1;
                i++;
                continue;
            }
            if (ch == '-')
            {
                sign = -1;
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }

            var hasNumber = i > start;
            var numberText = hasNumber ? text.Substring(start, i - start) : string.Empty;

            // Dice term
            if (i < text.Length && (text[i] == 'd' || text[i] == 'D'))
            {
                i++;
                var sidesStart = i;
                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                if (i == sidesStart)
                {
                    error = "Missing die sides (e.g. d6).";
                    return false;
                }

                if (!int.TryParse(text.AsSpan(sidesStart, i - sidesStart), out var sides) || sides is < 2 or > 100)
                {
                    error = "Invalid die sides.";
                    return false;
                }

                var count = 1;
                if (hasNumber && (!int.TryParse(numberText, out count) || count <= 0))
                {
                    error = "Invalid dice count.";
                    return false;
                }

                terms.Add(new DiceRollDiceTerm(sides, count, sign));
                continue;
            }

            // Integer constant
            if (!hasNumber)
            {
                error = "Invalid roll expression.";
                return false;
            }

            if (!int.TryParse(numberText, out var constant))
            {
                error = "Invalid number.";
                return false;
            }

            constantTotal += sign * constant;
        }

        if (terms.Count == 0)
        {
            return false;
        }

        var totalDice = terms.Sum(t => t.Count);
        if (totalDice > MaxTrayDiceCount)
        {
            error = $"3D tray supports up to {MaxTrayDiceCount} dice per roll.";
            return false;
        }

        // d100 is implemented as a paired d10 (tens/ones) per requestId.
        // Supporting multiple d100 in a single request would require request grouping.
        var d100Terms = terms.Where(t => t.Sides == 100).ToList();
        if (d100Terms.Count > 0)
        {
            if (d100Terms.Count != 1 || d100Terms[0].Count != 1)
            {
                error = "3D tray currently supports only 1d100 per roll.";
                return false;
            }
        }

        return true;
    }

    public void UpdateDieRotation(int sides, Quaternion rotation)
    {
        if (sides is < 2 or > 100)
        {
            return;
        }

        if (rotation.LengthSquared() < 1e-6f)
        {
            return;
        }

        rotation = Quaternion.Normalize(rotation);
        _rotationsBySides[sides] = new DiceDieRotation(sides, rotation.X, rotation.Y, rotation.Z, rotation.W);
        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void Roll()
    {
        LastErrorText = null;

        // Physics-driven tray roll for simple additive/subtractive dice expressions.
        if (TryParseTrayExpression(Expression, out var parsedTerms, out var constantTotal, out var parseError))
        {
            if (!string.IsNullOrWhiteSpace(parseError))
            {
                LastErrorText = parseError;
                return;
            }

            // Merge like terms for simpler aggregation + display, but preserve the user's term order.
            var mergedCounts = new Dictionary<(int Sides, int Sign), (int FirstIndex, int Count)>();
            for (var idx = 0; idx < parsedTerms.Count; idx++)
            {
                var t = parsedTerms[idx];
                var key = (t.Sides, t.Sign);
                if (mergedCounts.TryGetValue(key, out var existing))
                {
                    mergedCounts[key] = (existing.FirstIndex, existing.Count + t.Count);
                }
                else
                {
                    mergedCounts[key] = (idx, t.Count);
                }
            }

            var merged = mergedCounts
                .OrderBy(kvp => kvp.Value.FirstIndex)
                .Select(kvp => new DiceRollDiceTerm(kvp.Key.Sides, kvp.Value.Count, kvp.Key.Sign))
                .ToList();

            RollId = ++_rollIdCounter;
            _lastIssuedRollRequest = new DiceRollRequest(RequestId: RollId, Terms: merged, Direction: RollDirection);
            _pendingTrayRollsByRequestId[RollId] = new PendingTrayRoll
            {
                Request = _lastIssuedRollRequest,
                Terms = merged
                    .Select(t => new PendingTrayRollTerm
                    {
                        Sides = t.Sides,
                        Count = t.Count,
                        Sign = t.Sign,
                        Values = new List<int>(capacity: Math.Clamp(t.Count, 1, MaxTrayDiceCount)),
                    })
                    .ToList(),
                ConstantTotal = constantTotal,
            };

            var previewParts = new List<string>();
            foreach (var t in merged)
            {
                var termText = $"{t.Count}d{t.Sides}";
                if (previewParts.Count == 0)
                {
                    previewParts.Add(t.Sign < 0 ? "-" + termText : termText);
                }
                else
                {
                    previewParts.Add((t.Sign < 0 ? "-" : "+") + termText);
                }
            }

            if (constantTotal != 0)
            {
                previewParts.Add((constantTotal < 0 ? "-" : "+") + Math.Abs(constantTotal));
            }

            LastResultText = $"Rolling {string.Join(string.Empty, previewParts)}...";
            StateChanged?.Invoke();
            return;
        }

        if (!string.IsNullOrWhiteSpace(parseError))
        {
            LastErrorText = parseError;
            return;
        }

        _lastIssuedRollRequest = null;

        if (!DiceExpressionEvaluator.TryEvaluate(Expression, _rng, out var result, out var error))
        {
            LastErrorText = string.IsNullOrWhiteSpace(error) ? "Invalid dice expression." : error;
            return;
        }

        LastResultText = result.DisplayText;
        RollId = ++_rollIdCounter;
        History.Insert(0, LastResultText);
        while (History.Count > 20)
        {
            History.RemoveAt(History.Count - 1);
        }

        StateChanged?.Invoke();
    }

    private void OnSingleDieRollCompleted(long requestId, int sides, int value)
    {
        if (!_pendingTrayRollsByRequestId.TryGetValue(requestId, out var pending))
        {
            return;
        }

        var term = pending.Terms.FirstOrDefault(t => t.Sides == sides && t.Values.Count < t.Count);
        if (term is null)
        {
            return;
        }

        LastErrorText = null;
        term.Values.Add(value);

        if (pending.Terms.Any(t => t.Values.Count < t.Count))
        {
            return;
        }

        _pendingTrayRollsByRequestId.Remove(requestId);

        // Special-case d100 display when it's the only term and has exactly one value.
        if (pending.Terms.Count == 1 && pending.Terms[0].Sides == 100 && pending.Terms[0].Values.Count == 1 && pending.ConstantTotal == 0)
        {
            var v = pending.Terms[0].Values[0];
            LastResultText = $"1d100({v}) = {v}%";
        }
        else
        {
            var parts = new List<string>();
            foreach (var t in pending.Terms)
            {
                var label = $"{t.Count}d{t.Sides}";
                var valuesText = $"({string.Join(",", t.Values)})";
                var termText = label + valuesText;

                if (parts.Count == 0)
                {
                    parts.Add(t.Sign < 0 ? "-" + termText : termText);
                }
                else
                {
                    parts.Add((t.Sign < 0 ? " - " : " + ") + termText);
                }
            }

            if (pending.ConstantTotal != 0)
            {
                parts.Add((pending.ConstantTotal < 0 ? " - " : " + ") + Math.Abs(pending.ConstantTotal));
            }

            var diceTotal = pending.Terms.Sum(t => t.Sign * t.Values.Sum());
            var total = diceTotal + pending.ConstantTotal;
            LastResultText = $"{string.Concat(parts)} = {total}";
        }

        History.Insert(0, LastResultText);
        while (History.Count > 20)
        {
            History.RemoveAt(History.Count - 1);
        }

        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void RollSingleDie(object? parameter)
    {
        if (parameter is null)
        {
            return;
        }

        if (!int.TryParse(parameter.ToString(), out var sides) || sides is < 2 or > 100)
        {
            return;
        }

        Expression = $"1d{sides}";
        Roll();
    }

    [RelayCommand]
    private void SetRollDirection(object? parameter)
    {
        if (parameter is null)
        {
            return;
        }

        if (Enum.TryParse<DiceRollDirection>(parameter.ToString(), ignoreCase: true, out var dir))
        {
            RollDirection = dir;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        LastErrorText = null;
        LastResultText = string.Empty;
        History.Clear();
        _rotationsBySides.Clear();
        _pendingTrayRollsByRequestId.Clear();
        _lastIssuedRollRequest = null;
        _clearDiceIdCounter++;
        StateChanged?.Invoke();
    }

    private void NotifyDiceVisualConfigChanged()
    {
        _visualConfigRevision++;
        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void ResetDiceConfiguration()
    {
        if (DiceVisualConfigs.All(c => Math.Abs(c.DieScale - 1.0) < 1e-9 && Math.Abs(c.NumberScale - 1.0) < 1e-9))
        {
            return;
        }

        foreach (var cfg in DiceVisualConfigs)
        {
            cfg.DieScale = 1.0;
            cfg.NumberScale = 1.0;
        }
    }

    partial void OnRollDirectionChanged(DiceRollDirection value)
    {
        OnPropertyChanged(nameof(IsRollDirectionRight));
        OnPropertyChanged(nameof(IsRollDirectionLeft));
        OnPropertyChanged(nameof(IsRollDirectionUp));
        OnPropertyChanged(nameof(IsRollDirectionDown));
        OnPropertyChanged(nameof(IsRollDirectionRandom));
        StateChanged?.Invoke();
    }

    partial void OnShowDiceConfigurationChanged(bool value)
    {
        StateChanged?.Invoke();
    }

    partial void OnShowDebugInfoChanged(bool value)
    {
        StateChanged?.Invoke();
    }
}
