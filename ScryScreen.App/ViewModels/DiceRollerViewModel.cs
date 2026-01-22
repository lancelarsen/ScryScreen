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
    private long _trayRequestIdCounter;
    private long _trayBatchIdCounter;
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

    private sealed class PendingTrayRollBatch
    {
        public required long BatchId { get; init; }
        public required string BatchExpression { get; init; }
        public required List<long> RequestIdsInOrder { get; init; }
        public Dictionary<long, int> TotalsByRequestId { get; } = new();
        public Dictionary<long, string> DetailsByRequestId { get; } = new();
    }

    private readonly Dictionary<long, PendingTrayRoll> _pendingTrayRollsByRequestId = new();
    private readonly Dictionary<long, long> _trayBatchIdByRequestId = new();
    private readonly Dictionary<long, PendingTrayRollBatch> _pendingTrayRollBatchesByBatchId = new();
    private readonly List<DiceRollRequest> _issuedRollRequests = new();

    private const int MaxIssuedRollRequestsToKeep = 50;
    private const int MaxPendingTrayRequests = 100;

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
            _issuedRollRequests,
            _clearDiceIdCounter,
            DebugVisible: ShowDebugInfo);
    }

    private void TrackIssuedRequests(IEnumerable<DiceRollRequest> requests)
    {
        foreach (var r in requests)
        {
            _issuedRollRequests.Add(r);
        }

        if (_issuedRollRequests.Count > MaxIssuedRollRequestsToKeep)
        {
            _issuedRollRequests.RemoveRange(0, _issuedRollRequests.Count - MaxIssuedRollRequestsToKeep);
        }
    }

    private void TrimPendingIfNeeded()
    {
        if (_pendingTrayRollsByRequestId.Count <= MaxPendingTrayRequests)
        {
            return;
        }

        var oldest = _pendingTrayRollsByRequestId.Keys.OrderBy(x => x).Take(_pendingTrayRollsByRequestId.Count - MaxPendingTrayRequests).ToList();
        foreach (var requestId in oldest)
        {
            _pendingTrayRollsByRequestId.Remove(requestId);
            if (_trayBatchIdByRequestId.TryGetValue(requestId, out var batchId))
            {
                _trayBatchIdByRequestId.Remove(requestId);
                // Best-effort cleanup: if this makes the batch impossible to complete, leave it; it will be overwritten by later batches.
                if (_pendingTrayRollBatchesByBatchId.TryGetValue(batchId, out var batch))
                {
                    batch.TotalsByRequestId.Remove(requestId);
                    batch.DetailsByRequestId.Remove(requestId);
                }
            }
        }
    }

    private static List<DiceRollDiceTerm> MergeLikeTermsPreserveOrder(List<DiceRollDiceTerm> parsedTerms)
    {
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

        return mergedCounts
            .OrderBy(kvp => kvp.Value.FirstIndex)
            .Select(kvp => new DiceRollDiceTerm(kvp.Key.Sides, kvp.Value.Count, kvp.Key.Sign))
            .ToList();
    }

    private static string BuildPreviewText(List<DiceRollDiceTerm> terms, int constantTotal)
    {
        var previewParts = new List<string>();
        foreach (var t in terms)
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

        return string.Join(string.Empty, previewParts);
    }

    private static bool TryParseTrayBatchExpression(
        string? expression,
        out List<(string GroupExpression, List<DiceRollDiceTerm> Terms, int ConstantTotal, string Preview)> groups,
        out string? batchExpression,
        out string? error)
    {
        groups = new List<(string GroupExpression, List<DiceRollDiceTerm> Terms, int ConstantTotal, string Preview)>();
        batchExpression = null;
        error = null;

        var text = (expression ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        static string Normalize(string s) => (s ?? string.Empty).Replace(" ", string.Empty);

        var rawGroups = text.Split(',', StringSplitOptions.TrimEntries);
        if (rawGroups.Length == 0)
        {
            return false;
        }

        // Prevent pathological input from spawning huge numbers of dice.
        const int maxGroups = 10;
        if (rawGroups.Length > maxGroups)
        {
            error = $"3D tray supports up to {maxGroups} comma-separated rolls.";
            return false;
        }

        var totalDiceAllGroups = 0;

        var normalizedGroups = rawGroups.Select(Normalize).ToArray();
        batchExpression = string.Join(",", normalizedGroups);

        foreach (var raw in rawGroups)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "Invalid roll expression.";
                return false;
            }

            var groupExpression = Normalize(raw);

            if (!TryParseTrayExpression(raw, out var parsedTerms, out var constantTotal, out var parseError))
            {
                error = string.IsNullOrWhiteSpace(parseError) ? "Invalid roll expression." : parseError;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(parseError))
            {
                error = parseError;
                return false;
            }

            var merged = MergeLikeTermsPreserveOrder(parsedTerms);
            var diceCount = merged.Sum(t => t.Count);
            totalDiceAllGroups += diceCount;

            groups.Add((groupExpression, merged, constantTotal, BuildPreviewText(merged, constantTotal)));
        }

        const int maxTotalDice = 100;
        if (totalDiceAllGroups > maxTotalDice)
        {
            error = $"3D tray supports up to {maxTotalDice} total dice across comma-separated rolls.";
            return false;
        }

        return groups.Count > 0;
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

        // Comma-separated batch rolls: "4d6+2d4,2d6,3d4+10".
        if ((Expression ?? string.Empty).Contains(',', StringComparison.Ordinal))
        {
            if (TryParseTrayBatchExpression(Expression, out var groups, out var batchExpression, out var batchError))
            {
                if (!string.IsNullOrWhiteSpace(batchError))
                {
                    LastErrorText = batchError;
                    return;
                }

                if (string.IsNullOrWhiteSpace(batchExpression))
                {
                    LastErrorText = "Invalid roll expression.";
                    return;
                }

            var batchId = ++_trayBatchIdCounter;
            var requests = new List<DiceRollRequest>(capacity: groups.Count);
            var requestIdsInOrder = new List<long>(capacity: groups.Count);

            foreach (var g in groups)
            {
                var requestId = ++_trayRequestIdCounter;
                requestIdsInOrder.Add(requestId);

                    var req = new DiceRollRequest(RequestId: requestId, Terms: g.Terms, Direction: RollDirection);
                requests.Add(req);

                _pendingTrayRollsByRequestId[requestId] = new PendingTrayRoll
                {
                    Request = req,
                        Terms = g.Terms
                        .Select(t => new PendingTrayRollTerm
                        {
                            Sides = t.Sides,
                            Count = t.Count,
                            Sign = t.Sign,
                            Values = new List<int>(capacity: Math.Clamp(t.Count, 1, MaxTrayDiceCount)),
                        })
                        .ToList(),
                        ConstantTotal = g.ConstantTotal,
                };
                _trayBatchIdByRequestId[requestId] = batchId;
            }

            _pendingTrayRollBatchesByBatchId[batchId] = new PendingTrayRollBatch
            {
                BatchId = batchId,
                    BatchExpression = batchExpression,
                RequestIdsInOrder = requestIdsInOrder,
            };

                TrackIssuedRequests(requests);
                TrimPendingIfNeeded();
                RollId = ++_rollIdCounter;
                LastResultText = $"Rolling {groups.Count} totals...";
                StateChanged?.Invoke();
                return;
            }

            if (!string.IsNullOrWhiteSpace(batchError))
            {
                LastErrorText = batchError;
                return;
            }
        }

        // Physics-driven tray roll for simple additive/subtractive dice expressions.
        if (TryParseTrayExpression(Expression, out var parsedTerms, out var constantTotal, out var parseError))
        {
            if (!string.IsNullOrWhiteSpace(parseError))
            {
                LastErrorText = parseError;
                return;
            }

            var merged = MergeLikeTermsPreserveOrder(parsedTerms);

            var batchId = ++_trayBatchIdCounter;
            var requestId = ++_trayRequestIdCounter;
            var req = new DiceRollRequest(RequestId: requestId, Terms: merged, Direction: RollDirection);
            TrackIssuedRequests(new[] { req });

            _pendingTrayRollsByRequestId[requestId] = new PendingTrayRoll
            {
                Request = req,
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

            _trayBatchIdByRequestId[requestId] = batchId;
            _pendingTrayRollBatchesByBatchId[batchId] = new PendingTrayRollBatch
            {
                BatchId = batchId,
                BatchExpression = BuildPreviewText(merged, constantTotal),
                RequestIdsInOrder = new List<long> { requestId },
            };

            TrimPendingIfNeeded();

            RollId = ++_rollIdCounter;
            LastResultText = $"Rolling {BuildPreviewText(merged, constantTotal)}...";
            StateChanged?.Invoke();
            return;
        }

        if (!string.IsNullOrWhiteSpace(parseError))
        {
            LastErrorText = parseError;
            return;
        }

        // Non-tray evaluator path doesn't emit tray requests.

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

        var diceTotal = pending.Terms.Sum(t => t.Sign * t.Values.Sum());
        var total = diceTotal + pending.ConstantTotal;

        if (!_trayBatchIdByRequestId.TryGetValue(requestId, out var batchId)
            || !_pendingTrayRollBatchesByBatchId.TryGetValue(batchId, out var batch))
        {
            return;
        }

        batch.TotalsByRequestId[requestId] = total;

        // For batch display, record a compact per-group summary once the group is complete.
        if (batch.RequestIdsInOrder.Count > 1)
        {
            // Values are shown in term order; sign affects total, not the physical results.
            var orderedValues = pending.Terms.SelectMany(t => t.Values).ToList();
            var valuesText = orderedValues.Count == 0 ? "()" : $"({string.Join(",", orderedValues)})";
            var preview = BuildPreviewText(pending.Request.Terms.ToList(), pending.ConstantTotal);
            var detail = $"{preview} {valuesText} = {total}";
            batch.DetailsByRequestId[requestId] = detail;
        }

        // If this is a single-group roll, keep the detailed result text.
        if (batch.RequestIdsInOrder.Count == 1)
        {
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

                LastResultText = $"{string.Concat(parts)} = {total}";
            }

            History.Insert(0, LastResultText);
            while (History.Count > 20)
            {
                History.RemoveAt(History.Count - 1);
            }

            // Cleanup finished batch/request bookkeeping.
            _pendingTrayRollBatchesByBatchId.Remove(batchId);
            _trayBatchIdByRequestId.Remove(requestId);

            StateChanged?.Invoke();
            return;
        }

        // Multi-group batch: once all groups are done, display the group details in order.
        if (batch.RequestIdsInOrder.All(id => batch.TotalsByRequestId.ContainsKey(id)))
        {
            var details = batch.RequestIdsInOrder
                .Select(id => batch.DetailsByRequestId.TryGetValue(id, out var d) ? d : batch.TotalsByRequestId[id].ToString())
                .ToList();

            LastResultText = string.Join(", ", details);

            History.Insert(0, LastResultText);
            while (History.Count > 20)
            {
                History.RemoveAt(History.Count - 1);
            }

            // Cleanup finished batch.
            _pendingTrayRollBatchesByBatchId.Remove(batchId);
            foreach (var id in batch.RequestIdsInOrder)
            {
                _trayBatchIdByRequestId.Remove(id);
            }

            StateChanged?.Invoke();
        }
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
        _pendingTrayRollBatchesByBatchId.Clear();
        _trayBatchIdByRequestId.Clear();
        _issuedRollRequests.Clear();
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
