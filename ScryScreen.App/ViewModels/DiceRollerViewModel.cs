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
    private static readonly HashSet<int> TraySupportedSides = new() { 4, 6, 8, 10, 12, 20, 100 };

    private enum D20PickMode
    {
        None = 0,
        Advantage = 1,
        Disadvantage = 2,
    }

    private enum D100TensPickMode
    {
        None = 0,
        Advantage = 1,
        Disadvantage = 2,
    }

    private static bool TryParseD20PickExpression(string? expression, out D20PickMode mode, out int modifier, out string? error)
    {
        mode = D20PickMode.None;
        modifier = 0;
        error = null;

        var text = (expression ?? string.Empty).Replace(" ", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Accept: d20A, d20D, 1d20A, 1d20D, with optional +/- integer modifier.
        // Examples: d20A, d20D+5, 1d20a-2
        var index = 0;
        if (text.StartsWith("1", StringComparison.Ordinal))
        {
            index = 1;
        }

        if (text.Length < index + 4)
        {
            return false;
        }

        if (char.ToLowerInvariant(text[index]) != 'd')
        {
            return false;
        }

        if (!text.AsSpan(index + 1, 2).SequenceEqual("20"))
        {
            return false;
        }

        var tagIndex = index + 3;
        if (tagIndex >= text.Length)
        {
            return false;
        }

        var tag = char.ToUpperInvariant(text[tagIndex]);
        if (tag != 'A' && tag != 'D')
        {
            return false;
        }

        mode = tag == 'A' ? D20PickMode.Advantage : D20PickMode.Disadvantage;

        var modifierStart = tagIndex + 1;
        if (modifierStart >= text.Length)
        {
            modifier = 0;
            return true;
        }

        // Only allow modifiers like +5 or -2. If there's other trailing content (e.g. comma batch),
        // this is not a standalone d20A/d20D expression.
        if (text[modifierStart] != '+' && text[modifierStart] != '-')
        {
            return false;
        }

        var modText = text.Substring(modifierStart);
        if (!int.TryParse(modText, out modifier))
        {
            error = "Invalid modifier.";
        }

        return true;
    }

    private static bool TryParseD100TensPickExpression(
        string? expression,
        out D100TensPickMode mode,
        out int pickCount,
        out int modifier,
        out string? error)
    {
        mode = D100TensPickMode.None;
        pickCount = 0;
        modifier = 0;
        error = null;

        var text = (expression ?? string.Empty).Replace(" ", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Accept: d100A, d100AA, d100AAA, d100D, d100DD, d100DDD (and optional leading 1)
        // with optional +/- integer modifier.
        // The tens die is rolled (1 + pickCount) times; for A pick the lowest tens; for D pick the highest tens.
        var index = 0;
        if (text.StartsWith("1", StringComparison.Ordinal))
        {
            index = 1;
        }

        if (text.Length < index + 5)
        {
            return false;
        }

        if (char.ToLowerInvariant(text[index]) != 'd')
        {
            return false;
        }

        if (!text.AsSpan(index + 1, 3).SequenceEqual("100"))
        {
            return false;
        }

        var tagIndex = index + 4;
        if (tagIndex >= text.Length)
        {
            return false;
        }

        var tag = char.ToUpperInvariant(text[tagIndex]);
        if (tag != 'A' && tag != 'D')
        {
            return false;
        }

        mode = tag == 'A' ? D100TensPickMode.Advantage : D100TensPickMode.Disadvantage;

        var cursor = tagIndex;
        var count = 0;
        while (cursor < text.Length && char.ToUpperInvariant(text[cursor]) == tag)
        {
            count++;
            cursor++;
            if (count > 3)
            {
                error = "Too many A/D markers (max 3).";
                return true;
            }
        }

        pickCount = count;
        if (pickCount <= 0)
        {
            return false;
        }

        if (cursor >= text.Length)
        {
            modifier = 0;
            return true;
        }

        if (text[cursor] != '+' && text[cursor] != '-')
        {
            return false;
        }

        var modText = text.Substring(cursor);
        if (!int.TryParse(modText, out modifier))
        {
            error = "Invalid modifier.";
        }

        return true;
    }

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
        public D20PickMode D20Mode { get; init; }
        public D100TensPickMode D100Mode { get; init; }
        public int D100PickCount { get; init; }
        public int D100Modifier { get; init; }
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

    private double overlayOpacity = 0.65;

    private DiceRollerResultFontSize resultFontSize = DiceRollerResultFontSize.Medium;

    private bool resultsVisible = true;

    public event Action? StateChanged;

    public DiceRollerViewModel()
    {
        Expression = "d20";

        OverlayOpacity = 0.65;
        ResultFontSize = DiceRollerResultFontSize.Medium;

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

    public IReadOnlyList<DiceRollerResultFontSize> ResultFontSizes { get; } = new[]
    {
        DiceRollerResultFontSize.Small,
        DiceRollerResultFontSize.Medium,
        DiceRollerResultFontSize.Large,
    };

    public bool ResultsVisible
    {
        get => resultsVisible;
        set
        {
            if (resultsVisible == value)
            {
                return;
            }

            resultsVisible = value;
            OnPropertyChanged(nameof(ResultsVisible));
            StateChanged?.Invoke();
        }
    }

    public ObservableCollection<string> History { get; } = new();

    public ObservableCollection<DiceDieVisualConfigViewModel> DiceVisualConfigs { get; }

    [ObservableProperty]
    private string expression = "d20";

    [ObservableProperty]
    private string lastResultText = string.Empty;

    [ObservableProperty]
    private string? lastErrorText;

    [ObservableProperty]
    private bool showDiceConfiguration;

    [ObservableProperty]
    private bool showDebugInfo;

    public double OverlayOpacity
    {
        get => overlayOpacity;
        set
        {
            var clamped = Clamp(value, 0.0, 1.0);
            if (Math.Abs(overlayOpacity - clamped) < 1e-9)
            {
                return;
            }

            overlayOpacity = clamped;
            OnPropertyChanged(nameof(OverlayOpacity));
            StateChanged?.Invoke();
        }
    }

    public DiceRollerResultFontSize ResultFontSize
    {
        get => resultFontSize;
        set
        {
            if (resultFontSize == value)
            {
                return;
            }

            resultFontSize = value;
            OnPropertyChanged(nameof(ResultFontSize));
            StateChanged?.Invoke();
        }
    }

    [ObservableProperty]
    private DiceRollDirection rollDirection = DiceRollDirection.Right;

    [ObservableProperty]
    private long rollId;

    // When false, we still allow GM rolls and history, but we must not emit WebView/tray requests.
    [ObservableProperty]
    private bool isTrayAssigned;

    partial void OnIsTrayAssignedChanged(bool value)
    {
        if (!value)
        {
            CancelPendingTrayRolls();
        }
    }

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
            OverlayOpacity,
            _issuedRollRequests,
            _clearDiceIdCounter,
            ResultsVisible,
            ResultFontSize,
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
            var termText = t.Count == 1 ? $"d{t.Sides}" : $"{t.Count}d{t.Sides}";
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

        Expression = (Expression ?? string.Empty).Trim();

        var isCommaBatch = (Expression ?? string.Empty).Contains(',', StringComparison.Ordinal);

        // Special shorthand: d100A/d100AA/d100AAA and d100D/d100DD/d100DDD with optional +/- modifier.
        // These can be tray-driven when assigned; otherwise RNG-only.
        if (!isCommaBatch
            && TryParseD100TensPickExpression(Expression, out var d100Mode, out var d100PickCount, out var d100Modifier, out var d100Error))
        {
            if (!string.IsNullOrWhiteSpace(d100Error))
            {
                LastErrorText = d100Error;
                return;
            }

            RollD100TensPick(d100Mode, d100PickCount, d100Modifier);
            return;
        }

        // Special shorthand: d20A/d20D (advantage/disadvantage) with optional +/- modifier.
        if (!isCommaBatch && TryParseD20PickExpression(Expression, out var pickMode, out var pickModifier, out var pickError))
        {
            if (!string.IsNullOrWhiteSpace(pickError))
            {
                LastErrorText = pickError;
                return;
            }

            RollD20Pick(pickMode, pickModifier);
            return;
        }

        // If the tray isn't assigned to any portal, do not emit any tray/WebView requests.
        // Still support clicking dice + writing to history by evaluating locally.
        if (!IsTrayAssigned)
        {
            CancelPendingTrayRolls();
            EvaluateWithoutTrayAndCommit(Expression);
            return;
        }

        // Comma-separated batch rolls: "4d6+2d4,2d6,3d4+10".
        if (isCommaBatch)
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

                // If any group uses dice we don't have 3D models for, fall back to a normal RNG roll
                // (still show in history, but don't try to drive the WebView tray).
                if (groups.Any(g => g.Terms.Any(t => !TraySupportedSides.Contains(t.Sides))))
                {
                    EvaluateWithoutTrayAndCommit(Expression);
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

            // Not tray-parsable as a batch; attempt a normal RNG roll (this also supports commas).
            EvaluateWithoutTrayAndCommit(Expression);
            return;
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

            // If the expression uses dice we don't have 3D models for (e.g., d7), don't emit tray requests.
            if (merged.Any(t => !TraySupportedSides.Contains(t.Sides)))
            {
                EvaluateWithoutTrayAndCommit(Expression);
                return;
            }

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

        EvaluateWithoutTrayAndCommit(Expression);
    }

    private void EvaluateWithoutTrayAndCommit(string? expression)
    {
        if (!TryEvaluateWithoutTray(expression, out var displayText, out var evalError))
        {
            LastErrorText = string.IsNullOrWhiteSpace(evalError) ? "Invalid dice expression." : evalError;
            return;
        }

        LastResultText = displayText;
        RollId = ++_rollIdCounter;
        History.Insert(0, LastResultText);
        while (History.Count > 20)
        {
            History.RemoveAt(History.Count - 1);
        }

        StateChanged?.Invoke();
    }

    private void CancelPendingTrayRolls()
    {
        _pendingTrayRollsByRequestId.Clear();
        _pendingTrayRollBatchesByBatchId.Clear();
        _trayBatchIdByRequestId.Clear();
        _issuedRollRequests.Clear();
    }

    private bool TryEvaluateWithoutTray(string? expression, out string displayText, out string? error)
    {
        displayText = string.Empty;
        error = null;

        var text = expression ?? string.Empty;

        // Support comma-separated batch expressions even without the tray.
        if (text.Contains(',', StringComparison.Ordinal))
        {
            var parts = text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (parts.Count == 0)
            {
                error = "Invalid roll expression.";
                return false;
            }

            var results = new List<string>(capacity: parts.Count);
            foreach (var p in parts)
            {
                if (TryParseD100TensPickExpression(p, out var d100Mode, out var d100PickCount, out var d100Modifier, out var d100Error))
                {
                    if (!string.IsNullOrWhiteSpace(d100Error))
                    {
                        error = d100Error;
                        return false;
                    }

                    results.Add(EvaluateD100TensPickWithoutTray(d100Mode, d100PickCount, d100Modifier));
                    continue;
                }

                if (TryParseD20PickExpression(p, out var mode, out var mod, out var pickError))
                {
                    if (!string.IsNullOrWhiteSpace(pickError))
                    {
                        error = pickError;
                        return false;
                    }

                    var a = _rng.Next(1, 21);
                    var b = _rng.Next(1, 21);
                    var chosen = mode == D20PickMode.Advantage ? Math.Max(a, b) : Math.Min(a, b);
                    var totalValue = chosen + mod;
                    var tag = mode == D20PickMode.Advantage ? "A" : "D";
                    var modText = mod == 0 ? string.Empty : mod > 0 ? $" + {mod}" : $" - {Math.Abs(mod)}";
                    results.Add($"d20{tag} ({a},{b}){modText} = {totalValue}");
                    continue;
                }

                if (!DiceExpressionEvaluator.TryEvaluate(p, _rng, out var r, out var e))
                {
                    error = string.IsNullOrWhiteSpace(e) ? "Invalid dice expression." : e;
                    return false;
                }

                results.Add(r.DisplayText);
            }

            displayText = string.Join(", ", results);
            return true;
        }

        if (TryParseD100TensPickExpression(text, out var singleD100Mode, out var singleD100PickCount, out var singleD100Modifier, out var singleD100Error))
        {
            if (!string.IsNullOrWhiteSpace(singleD100Error))
            {
                error = singleD100Error;
                return false;
            }

            displayText = EvaluateD100TensPickWithoutTray(singleD100Mode, singleD100PickCount, singleD100Modifier);
            return true;
        }

        if (TryParseD20PickExpression(text, out var singleMode, out var singleMod, out var singlePickError))
        {
            if (!string.IsNullOrWhiteSpace(singlePickError))
            {
                error = singlePickError;
                return false;
            }

            var a = _rng.Next(1, 21);
            var b = _rng.Next(1, 21);
            var chosen = singleMode == D20PickMode.Advantage ? Math.Max(a, b) : Math.Min(a, b);
            var totalValue = chosen + singleMod;
            var tag = singleMode == D20PickMode.Advantage ? "A" : "D";
            var modText = singleMod == 0 ? string.Empty : singleMod > 0 ? $" + {singleMod}" : $" - {Math.Abs(singleMod)}";
            displayText = $"d20{tag} ({a},{b}){modText} = {totalValue}";
            return true;
        }

        if (!DiceExpressionEvaluator.TryEvaluate(expression, _rng, out var single, out var singleError))
        {
            error = singleError;
            return false;
        }

        displayText = single.DisplayText;
        return true;
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
            var valuesText = orderedValues.Count == 0 ? " ()" : $" ({string.Join(",", orderedValues)})";
            var preview = BuildPreviewText(pending.Request.Terms.ToList(), pending.ConstantTotal);
            var detail = $"{preview} {valuesText} = {total}";
            batch.DetailsByRequestId[requestId] = detail;
        }

        // If this is a single-group roll, keep the detailed result text.
        if (batch.RequestIdsInOrder.Count == 1)
        {
            // Special-case d100 A/D tens-pick: roll (1+pickCount) tens dice + one ones die, then pick tens.
            if (pending.D100Mode != D100TensPickMode.None
                && pending.Terms.Count == 2
                && pending.Terms.Any(t => t.Sides == 100)
                && pending.Terms.Any(t => t.Sides == 10))
            {
                var clampedPickCount = Math.Clamp(pending.D100PickCount, 1, 3);
                var tensTerm = pending.Terms.FirstOrDefault(t => t.Sides == 100);
                var onesTerm = pending.Terms.FirstOrDefault(t => t.Sides == 10);
                if (tensTerm is null || onesTerm is null)
                {
                    return;
                }

                if (tensTerm.Count != 1 + clampedPickCount || onesTerm.Count != 1)
                {
                    return;
                }

                if (tensTerm.Values.Count != tensTerm.Count || onesTerm.Values.Count != onesTerm.Count)
                {
                    return;
                }

                // d100 tens dice: ideally returns 10,20,...,90,100 (treat 100 as 00).
                // If the tray ever returns a 1..100 value, normalize to a decade.
                static int NormalizeTens(int v)
                {
                    if (v == 100)
                    {
                        return 0;
                    }

                    if (v % 10 != 0)
                    {
                        v = ((v - 1) / 10) * 10;
                    }

                    return Math.Clamp(v, 0, 90);
                }

                // d10 ones die: tray returns 1..10; treat 10 as 0.
                var ones = onesTerm.Values[0] % 10;
                var tensRolls = tensTerm.Values.Select(NormalizeTens).ToList();

                var pickedTens = pending.D100Mode == D100TensPickMode.Advantage ? tensRolls.Min() : tensRolls.Max();
                var baseValue = pickedTens + ones;
                if (baseValue == 0)
                {
                    baseValue = 100;
                }

                var totalValue = baseValue + pending.D100Modifier;

                var tag = pending.D100Mode == D100TensPickMode.Advantage ? 'A' : 'D';
                var suffix = new string(tag, clampedPickCount);
                var modText = pending.D100Modifier == 0
                    ? string.Empty
                    : pending.D100Modifier > 0 ? $" + {pending.D100Modifier}" : $" - {Math.Abs(pending.D100Modifier)}";

                // Keep TotalsByRequestId consistent for any downstream consumers.
                batch.TotalsByRequestId[requestId] = totalValue;

                var tensText = string.Join(",", tensRolls.Select(t => t.ToString("00")));
                LastResultText = $"d100{suffix} ({tensText} + {ones}){modText} = {totalValue}";
            }
            // Special-case d20 advantage/disadvantage: 2d20, keep highest/lowest.
            else if (pending.D20Mode != D20PickMode.None
                && pending.Terms.Count == 1
                && pending.Terms[0].Sides == 20
                && pending.Terms[0].Count == 2
                && pending.Terms[0].Values.Count == 2)
            {
                var a = pending.Terms[0].Values[0];
                var b = pending.Terms[0].Values[1];
                var chosen = pending.D20Mode == D20PickMode.Advantage ? Math.Max(a, b) : Math.Min(a, b);
                var tag = pending.D20Mode == D20PickMode.Advantage ? "A" : "D";

                var totalValue = chosen + pending.ConstantTotal;
                var modText = pending.ConstantTotal == 0
                    ? string.Empty
                    : pending.ConstantTotal > 0 ? $" + {pending.ConstantTotal}" : $" - {Math.Abs(pending.ConstantTotal)}";

                LastResultText = $"d20{tag} ({a},{b}){modText} = {totalValue}";
            }
            // Special-case d100 display when it's the only term and has exactly one value.
            else if (pending.Terms.Count == 1 && pending.Terms[0].Sides == 100 && pending.Terms[0].Values.Count == 1 && pending.ConstantTotal == 0)
            {
                var v = pending.Terms[0].Values[0];
                LastResultText = $"d100 ({v}) = {v}%";
            }
            else
            {
                var parts = new List<string>();
                foreach (var t in pending.Terms)
                {
                    var label = t.Count == 1 ? $"d{t.Sides}" : $"{t.Count}d{t.Sides}";
                    var valuesText = $" ({string.Join(",", t.Values)})";
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

        Expression = $"d{sides}";
        Roll();
    }

    [RelayCommand]
    private void RollD20Advantage()
    {
        Expression = "d20A";
        RollD20Pick(D20PickMode.Advantage, modifier: 0);
    }

    [RelayCommand]
    private void RollD20Disadvantage()
    {
        Expression = "d20D";
        RollD20Pick(D20PickMode.Disadvantage, modifier: 0);
    }

    private void RollD20Pick(D20PickMode mode, int modifier)
    {
        LastErrorText = null;

        if (!IsTrayAssigned)
        {
            CancelPendingTrayRolls();
            EvaluateD20PickWithoutTrayAndCommit(mode, modifier);
            return;
        }

        var terms = new List<DiceRollDiceTerm> { new DiceRollDiceTerm(20, 2, +1) };

        var batchId = ++_trayBatchIdCounter;
        var requestId = ++_trayRequestIdCounter;
        var req = new DiceRollRequest(RequestId: requestId, Terms: terms, Direction: RollDirection);
        TrackIssuedRequests(new[] { req });

        _pendingTrayRollsByRequestId[requestId] = new PendingTrayRoll
        {
            Request = req,
            Terms = new List<PendingTrayRollTerm>
            {
                new PendingTrayRollTerm
                {
                    Sides = 20,
                    Count = 2,
                    Sign = +1,
                    Values = new List<int>(capacity: 2),
                },
            },
            ConstantTotal = modifier,
            D20Mode = mode,
        };

        _trayBatchIdByRequestId[requestId] = batchId;
        _pendingTrayRollBatchesByBatchId[batchId] = new PendingTrayRollBatch
        {
            BatchId = batchId,
            BatchExpression = modifier == 0
                ? (mode == D20PickMode.Advantage ? "d20A" : "d20D")
                : (mode == D20PickMode.Advantage ? $"d20A{modifier:+#;-#}" : $"d20D{modifier:+#;-#}"),
            RequestIdsInOrder = new List<long> { requestId },
        };

        TrimPendingIfNeeded();
        RollId = ++_rollIdCounter;
        LastResultText = mode == D20PickMode.Advantage ? "Rolling d20 (advantage)..." : "Rolling d20 (disadvantage)...";
        StateChanged?.Invoke();
    }

    private void EvaluateD20PickWithoutTrayAndCommit(D20PickMode mode, int modifier)
    {
        var a = _rng.Next(1, 21);
        var b = _rng.Next(1, 21);
        var chosen = mode == D20PickMode.Advantage ? Math.Max(a, b) : Math.Min(a, b);
        var tag = mode == D20PickMode.Advantage ? "A" : "D";

        var totalValue = chosen + modifier;
        var modText = modifier == 0 ? string.Empty : modifier > 0 ? $" + {modifier}" : $" - {Math.Abs(modifier)}";
        LastResultText = $"d20{tag} ({a},{b}){modText} = {totalValue}";
        RollId = ++_rollIdCounter;
        History.Insert(0, LastResultText);
        while (History.Count > 20)
        {
            History.RemoveAt(History.Count - 1);
        }

        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void RollD100Advantage()
    {
        Expression = "d100A";
        RollD100TensPick(D100TensPickMode.Advantage, pickCount: 1, modifier: 0);
    }

    [RelayCommand]
    private void RollD100Advantage2()
    {
        Expression = "d100AA";
        RollD100TensPick(D100TensPickMode.Advantage, pickCount: 2, modifier: 0);
    }

    [RelayCommand]
    private void RollD100Disadvantage()
    {
        Expression = "d100D";
        RollD100TensPick(D100TensPickMode.Disadvantage, pickCount: 1, modifier: 0);
    }

    [RelayCommand]
    private void RollD100Disadvantage2()
    {
        Expression = "d100DD";
        RollD100TensPick(D100TensPickMode.Disadvantage, pickCount: 2, modifier: 0);
    }

    private void RollD100TensPick(D100TensPickMode mode, int pickCount, int modifier)
    {
        LastErrorText = null;

        if (!IsTrayAssigned)
        {
            CancelPendingTrayRolls();
            RollD100TensPickWithoutTrayAndCommit(mode, pickCount, modifier);
            return;
        }

        var clampedPickCount = Math.Clamp(pickCount, 1, 3);
        var tensDiceCount = 1 + clampedPickCount;

        // Percentile: roll (1+pickCount) tens dice + one ones die.
        // Use d100 dice for tens so the tray shows 00/10/20/...; use a single d10 for ones.
        var terms = new List<DiceRollDiceTerm>
        {
            new DiceRollDiceTerm(100, tensDiceCount, +1),
            new DiceRollDiceTerm(10, 1, +1),
        };

        var batchId = ++_trayBatchIdCounter;
        var requestId = ++_trayRequestIdCounter;
        var req = new DiceRollRequest(RequestId: requestId, Terms: terms, Direction: RollDirection);
        TrackIssuedRequests(new[] { req });

        _pendingTrayRollsByRequestId[requestId] = new PendingTrayRoll
        {
            Request = req,
            Terms = new List<PendingTrayRollTerm>
            {
                new PendingTrayRollTerm
                {
                    Sides = 100,
                    Count = tensDiceCount,
                    Sign = +1,
                    Values = new List<int>(capacity: tensDiceCount),
                },
                new PendingTrayRollTerm
                {
                    Sides = 10,
                    Count = 1,
                    Sign = +1,
                    Values = new List<int>(capacity: 1),
                },
            },
            ConstantTotal = 0,
            D100Mode = mode,
            D100PickCount = clampedPickCount,
            D100Modifier = modifier,
        };

        var tag = mode == D100TensPickMode.Advantage ? 'A' : 'D';
        var suffix = new string(tag, clampedPickCount);
        var expr = modifier == 0 ? $"d100{suffix}" : $"d100{suffix}{modifier:+#;-#}";

        _trayBatchIdByRequestId[requestId] = batchId;
        _pendingTrayRollBatchesByBatchId[batchId] = new PendingTrayRollBatch
        {
            BatchId = batchId,
            BatchExpression = expr,
            RequestIdsInOrder = new List<long> { requestId },
        };

        TrimPendingIfNeeded();
        RollId = ++_rollIdCounter;
        LastResultText = $"Rolling {expr}...";
        StateChanged?.Invoke();
    }

    private void RollD100TensPickWithoutTrayAndCommit(D100TensPickMode mode, int pickCount, int modifier)
    {
        LastErrorText = null;

        LastResultText = EvaluateD100TensPickWithoutTray(mode, pickCount, modifier);
        RollId = ++_rollIdCounter;
        History.Insert(0, LastResultText);
        while (History.Count > 20)
        {
            History.RemoveAt(History.Count - 1);
        }

        StateChanged?.Invoke();
    }

    private string EvaluateD100TensPickWithoutTray(D100TensPickMode mode, int pickCount, int modifier)
    {
        var count = Math.Clamp(pickCount, 1, 3);

        // Tens die gets rolled (1 + count) times.
        var tensRolls = new List<int>(capacity: 1 + count);
        for (var i = 0; i < 1 + count; i++)
        {
            tensRolls.Add(_rng.Next(0, 10) * 10);
        }

        var ones = _rng.Next(0, 10);

        var pickedTens = mode == D100TensPickMode.Advantage ? tensRolls.Min() : tensRolls.Max();
        var baseValue = pickedTens + ones;
        if (baseValue == 0)
        {
            baseValue = 100;
        }

        var totalValue = baseValue + modifier;

        var tag = mode == D100TensPickMode.Advantage ? 'A' : 'D';
        var suffix = new string(tag, count);

        var modText = modifier == 0 ? string.Empty : modifier > 0 ? $" + {modifier}" : $" - {Math.Abs(modifier)}";
        var tensText = string.Join(",", tensRolls.Select(t => t.ToString("00")));
        return $"d100{suffix} ({tensText} + {ones}){modText} = {totalValue}";
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
