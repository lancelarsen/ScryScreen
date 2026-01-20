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

    private readonly Dictionary<long, DiceRollRequest> _pendingSingleDieRollsByRequestId = new();
    private DiceRollRequest? _lastIssuedRollRequest;

    public event Action? StateChanged;

    public DiceRollerViewModel()
    {
        Expression = "1d20";
        OverlayOpacity = DiceRollerState.Default.OverlayOpacity;

        DiceRollerEventBus.SingleDieRollCompleted += OnSingleDieRollCompleted;
    }

    public ObservableCollection<string> History { get; } = new();

    [ObservableProperty]
    private string expression = "1d20";

    [ObservableProperty]
    private string lastResultText = string.Empty;

    [ObservableProperty]
    private string? lastErrorText;

    [ObservableProperty]
    private double overlayOpacity;

    [ObservableProperty]
    private long rollId;

    public DiceRollerState SnapshotState()
    {
        // Keep the portal overlay alive so dice can appear immediately when requested.
        var text = string.IsNullOrWhiteSpace(LastResultText) ? "Dice Roller" : LastResultText;
        var rotations = _rotationsBySides.Count == 0
            ? Array.Empty<DiceDieRotation>()
            : _rotationsBySides.Values.OrderBy(r => r.Sides).ToArray();
        return new(text, OverlayOpacity, RollId, rotations, _lastIssuedRollRequest, _clearDiceIdCounter);
    }

    private static bool TryParseSingleDie(string? expression, out int sides)
    {
        sides = 0;
        var text = (expression ?? string.Empty).Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Accept only "1dN" or "dN".
        if (text.StartsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            text = "1" + text;
        }

        var dIndex = text.IndexOf('d');
        if (dIndex < 0)
        {
            dIndex = text.IndexOf('D');
        }

        if (dIndex <= 0 || dIndex != 1)
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(0, dIndex), out var count) || count != 1)
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

        // Physics-driven single-die roll: the portal tray determines the final value.
        if (TryParseSingleDie(Expression, out var sides))
        {
            RollId = ++_rollIdCounter;
            _lastIssuedRollRequest = new DiceRollRequest(RequestId: RollId, Sides: sides);
            _pendingSingleDieRollsByRequestId[RollId] = _lastIssuedRollRequest;
            LastResultText = $"Rolling 1d{sides}...";
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
        if (!_pendingSingleDieRollsByRequestId.TryGetValue(requestId, out var req))
        {
            return;
        }

        if (req.Sides != sides)
        {
            return;
        }

        LastErrorText = null;
        _pendingSingleDieRollsByRequestId.Remove(requestId);

        LastResultText = $"1d{sides}({value}) = {value}";
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
    private void Clear()
    {
        LastErrorText = null;
        LastResultText = string.Empty;
        History.Clear();
        _rotationsBySides.Clear();
        _pendingSingleDieRollsByRequestId.Clear();
        _lastIssuedRollRequest = null;
        _clearDiceIdCounter++;
        StateChanged?.Invoke();
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        if (OverlayOpacity < 0) OverlayOpacity = 0;
        if (OverlayOpacity > 1) OverlayOpacity = 1;
        StateChanged?.Invoke();
    }
}
