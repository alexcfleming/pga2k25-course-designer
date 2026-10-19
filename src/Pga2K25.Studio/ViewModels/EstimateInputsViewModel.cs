using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Estimate;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Inputs to the construction estimate. Course choices (zone class per hole, fidelity, project values, course-wide
/// quantities) save to the course's Estimate.json; unit prices save as overrides shared by every course in
/// %AppData%\CourseDesignerStudio\estimate. A blank box means "use the default".
/// </summary>
public partial class EstimateInputsViewModel : ObservableObject
{
    public const string DefaultClass = "(default)";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly string _inputPath;
    readonly Func<Task> _rebuild;
    EstimateInput _input;

    public string InputPath => _inputPath;
    public string PricesPath => EstimateData.UserCostsPath;
    public string DefaultClassText { get; }
    public IReadOnlyList<string> ZoneChoices { get; }
    public IReadOnlyList<PresetRowViewModel> Presets { get; }
    public ObservableCollection<HoleZoneRowViewModel> Holes { get; } = [];
    public ObservableCollection<ValueRowViewModel> Project { get; } = [];
    public ObservableCollection<ValueRowViewModel> CourseWide { get; } = [];
    public ObservableCollection<PriceRowViewModel> Prices { get; } = [];
    public IReadOnlyList<string> StatusChoices { get; } = ["placeholder", "researched", "derived", "quote"];
    public int DrawnZones => _input.Zones.Count;

    [ObservableProperty] private double _fidelityOffset;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private IBrush _messageBrush = Brush.Parse("#1F4D30");
    [ObservableProperty] private bool _busy;

    public string FidelityText => FidelityOffset.ToString("+0.00;-0.00;0.00", Inv);
    partial void OnFidelityOffsetChanged(double value) => OnPropertyChanged(nameof(FidelityText));

    public EstimateInputsViewModel(string inputPath, IReadOnlyList<int> holes, Func<Task> rebuild)
    {
        _inputPath = inputPath;
        _rebuild = rebuild;
        _input = EstimateData.LoadInput(inputPath);
        var presets = EstimateData.LoadPresets();
        DefaultClassText = $"Default for play ground: {presets.Defaults.ManufacturedClass}";
        ZoneChoices = [DefaultClass, .. presets.Presets.Keys];
        Presets = presets.Presets.Select(p => new PresetRowViewModel(p.Key, p.Value)).ToList();
        FidelityOffset = _input.FidelityOffset;

        foreach (int n in holes)
            Holes.Add(new HoleZoneRowViewModel(n, _input.ForHole(n).ZoneClass ?? DefaultClass));

        var shippedProject = EstimateData.LoadProject();
        foreach (var (key, s) in shippedProject)
            Project.Add(new ValueRowViewModel(key, Label(key), Number(s.Value), _input.Project.TryGetValue(key, out var v) ? Number(v) : "",
                s.IsPlaceholder ? "placeholder" : s.Status));

        var shippedCosts = EstimateData.LoadUnitCosts();
        CourseWide.Add(new ValueRowViewModel("clearing", "Clearing and grubbing (acres)", "computed", Text(_input.CourseWide, "clearing"), ""));
        foreach (var key in new[] { "pump_station", "pond_liner", "central_control", "practice_area", "specialty", "grow_in" })
            CourseWide.Add(new ValueRowViewModel(key, shippedCosts.Get(key).Label, "1", Text(_input.CourseWide, key), ""));

        var mine = EstimateData.LoadUserCosts();
        foreach (var (key, c) in shippedCosts.Items)
            Prices.Add(new PriceRowViewModel(key, c, mine.TryGetValue(key, out var m) ? m : null, StatusChoices));
    }

    static string Text(Dictionary<string, double> d, string key) => d.TryGetValue(key, out var v) ? Number(v) : "";
    static string Number(double v) => v.ToString("0.####", Inv);
    static string Label(string key) => char.ToUpperInvariant(key[0]) + key[1..].Replace('_', ' ');

    /// <summary>Blank → null; otherwise a number in either the invariant or the current culture.</summary>
    internal static bool TryNumber(string text, out double? value)
    {
        value = null;
        text = text.Trim().TrimStart('$').Replace(",", "");
        if (text.Length == 0) return true;
        if (double.TryParse(text, NumberStyles.Float, Inv, out var v) || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
        {
            value = v;
            return true;
        }
        return false;
    }

    /// <summary>Checks every box, then writes Estimate.json and the price overrides. Returns false (nothing written) on a bad entry.</summary>
    public bool Save()
    {
        var errors = new List<string>();
        var project = new Dictionary<string, double>();
        foreach (var r in Project)
            if (!TryNumber(r.Value, out var v)) errors.Add(r.Label);
            else if (v is { } x) project[r.Key] = x;
        var courseWide = new Dictionary<string, double>();
        foreach (var r in CourseWide)
            if (!TryNumber(r.Value, out var v) || v < 0) errors.Add(r.Label);
            else if (v is { } x) courseWide[r.Key] = x;

        var overrides = new Dictionary<string, UnitCost>();
        foreach (var p in Prices)
        {
            if (!p.TryBuild(out var cost)) { errors.Add($"{p.Label} price"); continue; }
            if (cost is not null) overrides[p.Key] = cost;
        }
        if (errors.Count > 0)
        {
            Show($"Not saved. Check: {string.Join(", ", errors)}.", error: true);
            return false;
        }

        _input = _input with
        {
            FidelityOffset = Math.Round(FidelityOffset, 2),
            Holes = Holes.Where(h => h.ZoneClass != DefaultClass)
                .ToDictionary(h => h.Hole.ToString(Inv), h => new HoleOverride { ZoneClass = h.ZoneClass }),
            Project = project,
            CourseWide = courseWide,
        };
        EstimateData.SaveInput(_inputPath, _input);
        EstimateData.SaveUserCosts(overrides);
        foreach (var p in Prices) p.MarkSaved();
        Show($"Saved {DateTime.Now:HH:mm}: course choices to Estimate.json, {overrides.Count} price override(s) for all courses.");
        return true;
    }

    void Show(string text, bool error = false)
    {
        Message = text;
        MessageBrush = Brush.Parse(error ? "#A32D2D" : "#1F4D30");
    }

    [RelayCommand] private void SaveInputs() => Save();

    [RelayCommand]
    private async Task SaveAndRebuild()
    {
        if (!Save()) return;
        Busy = true;
        try { await _rebuild(); }
        finally { Busy = false; }
    }

    [RelayCommand] private void ResetFidelity() => FidelityOffset = 0;
}

public sealed class PresetRowViewModel(string name, ZonePreset p)
{
    public string Name { get; } = name;
    public string Fidelity { get; } = p.NaturalFidelity.ToString("0.##", CultureInfo.InvariantCulture);
    public string Wavelength { get; } = p.BaselineWavelengthYd > 0 ? $"{p.BaselineWavelengthYd:0} yd" : "plane";
    public string Rock { get; } = $"×{p.RockFactor:0.#}";
    public string Drainage { get; } = $"×{p.DrainageNeed:0.#}";
    public string Clearing { get; } = $"{p.ClearingPct:P0}";
    public string Notes { get; } = p.Notes;
}

public partial class HoleZoneRowViewModel(int hole, string zoneClass) : ObservableObject
{
    public int Hole { get; } = hole;
    public string Title { get; } = $"Hole {hole}";
    [ObservableProperty] private string _zoneClass = zoneClass;
}

/// <summary>A number with a shipped default; the box holds this course's value, blank for the default.</summary>
public partial class ValueRowViewModel(string key, string label, string defaultText, string value, string status) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string DefaultText { get; } = defaultText;
    public string Status { get; } = status;
    [ObservableProperty] private string _value = value;
}

/// <summary>One unit price: the shipped value and, when set, the user's own (with its source, year and status).</summary>
public partial class PriceRowViewModel : ObservableObject
{
    readonly UnitCost _shipped;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Key { get; }
    public string Label => _shipped.Label;
    public string Unit => _shipped.Unit;
    public string ShippedText { get; }
    public IReadOnlyList<string> StatusChoices { get; }

    [ObservableProperty] private string _price;
    [ObservableProperty] private string _low;
    [ObservableProperty] private string _high;
    [ObservableProperty] private string _year;
    [ObservableProperty] private string _status;
    [ObservableProperty] private string _source;
    [ObservableProperty] private bool _isMine;

    public PriceRowViewModel(string key, UnitCost shipped, UnitCost? mine, IReadOnlyList<string> statusChoices)
    {
        Key = key;
        _shipped = shipped;
        StatusChoices = statusChoices;
        ShippedText = (shipped.Price is { } p ? p.ToString("0.##", Inv) : "—") + (shipped.IsPlaceholder ? " (placeholder)" : $" ({shipped.Status})");
        var c = mine ?? shipped;
        _price = N(c.Price); _low = N(c.Low); _high = N(c.High);
        _year = c.Year?.ToString(Inv) ?? "";
        _status = c.Status;
        _source = c.Source;
        _isMine = mine is not null;
    }

    static string N(double? v) => v?.ToString("0.####", Inv) ?? "";

    public string OriginText => IsMine ? "yours" : "shipped";
    partial void OnIsMineChanged(bool value) => OnPropertyChanged(nameof(OriginText));

    /// <summary>The user's override, or null when every field still matches the shipped value. False on a bad number.</summary>
    public bool TryBuild(out UnitCost? cost)
    {
        cost = null;
        if (!EstimateInputsViewModel.TryNumber(Price, out var price) || !EstimateInputsViewModel.TryNumber(Low, out var low)
            || !EstimateInputsViewModel.TryNumber(High, out var high) || !EstimateInputsViewModel.TryNumber(Year, out var year))
            return false;
        var built = _shipped with
        {
            Price = price, Low = low, High = high, Year = year is { } y ? (int)y : null,
            Status = Status, Source = Source.Trim(),
        };
        bool same = built.Price == _shipped.Price && built.Low == _shipped.Low && built.High == _shipped.High
            && built.Year == _shipped.Year && built.Status == _shipped.Status && built.Source == _shipped.Source.Trim();
        cost = same ? null : built;
        return true;
    }

    public void MarkSaved() => IsMine = TryBuild(out var c) && c is not null;

    /// <summary>Back to the shipped price (saved on the next Save).</summary>
    [RelayCommand]
    private void Reset()
    {
        Price = N(_shipped.Price); Low = N(_shipped.Low); High = N(_shipped.High);
        Year = _shipped.Year?.ToString(Inv) ?? "";
        Status = _shipped.Status;
        Source = _shipped.Source;
    }
}
