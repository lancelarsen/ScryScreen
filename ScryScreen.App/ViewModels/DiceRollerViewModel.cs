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

    private sealed class PendingTrayRoll
    {
        public required DiceRollRequest Request { get; init; }
        public required List<int> Values { get; init; }
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

    private static bool TryParseDicePool(string? expression, out int count, out int sides)
    {
        count = 0;
        sides = 0;
        var text = (expression ?? string.Empty).Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Accept only "NdS" or "dS".
        if (text.StartsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            text = "1" + text;
        }

        var dIndex = text.IndexOf('d');
        if (dIndex < 0)
        {
            dIndex = text.IndexOf('D');
        }

        if (dIndex <= 0)
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(0, dIndex), out count) || count <= 0)
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(dIndex + 1), out sides) || sides is < 2 or > 100)
        {
            return false;
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

        // Physics-driven tray roll for simple NdS expressions: the portal tray determines the final values.
        if (TryParseDicePool(Expression, out var count, out var sides))
        {
            if (count > MaxTrayDiceCount)
            {
                LastErrorText = $"3D tray supports up to {MaxTrayDiceCount} dice per roll.";
                return;
            }

            // d100 is implemented as a paired d10 (tens/ones) per requestId.
            // Supporting multiple d100 in a single request would require request grouping.
            if (sides == 100 && count != 1)
            {
                LastErrorText = "3D tray currently supports only 1d100 per roll.";
                return;
            }

            RollId = ++_rollIdCounter;
            _lastIssuedRollRequest = new DiceRollRequest(RequestId: RollId, Sides: sides, Count: count, Direction: RollDirection);
            _pendingTrayRollsByRequestId[RollId] = new PendingTrayRoll
            {
                Request = _lastIssuedRollRequest,
                Values = new List<int>(capacity: Math.Clamp(count, 1, MaxTrayDiceCount)),
            };
            LastResultText = sides == 100
                ? "Rolling 1d100 (percentile)..."
                : $"Rolling {count}d{sides}...";
            StateChanged?.Invoke();
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

        var req = pending.Request;
        if (req.Sides != sides)
        {
            return;
        }

        if (pending.Values.Count >= req.Count)
        {
            return;
        }

        LastErrorText = null;

        pending.Values.Add(value);

        if (pending.Values.Count < req.Count)
        {
            return;
        }

        _pendingTrayRollsByRequestId.Remove(requestId);

        var values = pending.Values;
        var total = values.Sum();

        LastResultText = sides == 100
            ? $"1d100({value}) = {value}%"
            : (req.Count == 1
                ? $"1d{sides}({value}) = {value}"
                : $"{req.Count}d{sides}({string.Join(",", values)}) = {total}");
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
