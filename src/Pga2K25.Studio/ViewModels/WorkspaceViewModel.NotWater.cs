using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// "Not water in game": a click on a piece of modelled water marks it dry. The marks save to NotWater.json in the
/// output folder, the map re-surveys without that piece, and every product built afterwards leaves it out.
/// </summary>
public partial class WorkspaceViewModel
{
    const string NotWaterPin = "notwater:";

    List<NotWaterMark> _notWater = [];
    public ObservableCollection<NotWaterRowViewModel> NotWaterRows { get; } = [];

    [ObservableProperty] private bool _markingNotWater;
    [ObservableProperty] private bool _hasInlandWater;

    partial void OnMarkingNotWaterChanged(bool value)
    {
        if (value) { PlacingNote = false; HoverText = "Click a piece of water that is dry in game"; }
    }

    void LoadNotWater()
    {
        _notWater = NotWaterMarks.Load(OutputFolder);
        RebuildNotWaterRows();
    }

    void RebuildNotWaterRows()
    {
        NotWaterRows.Clear();
        foreach (var m in _notWater)
        {
            var mark = m;
            int? hole = NearestHole(m.Point);
            string where = hole is int h ? $"Near hole {h}" : "Away from the holes";
            NotWaterRows.Add(new NotWaterRowViewModel($"{where} · {m.Marked:d MMM}", () => RemoveNotWater(mark)));
        }
        LayersChanged?.Invoke();
    }

    void MarkNotWater(Point px)
    {
        if (_projection is null || _survey is null || !_mapRect.Contains(new SKPoint((float)px.X, (float)px.Y))) return;
        var w = _projection.Unmap(new SKPoint((float)px.X, (float)px.Y));
        if (_survey.InlandWater is not { } water || !NearWater(water, w))
        {
            HoverText = "No modelled water there: click inside the piece that's dry in game";
            return;
        }
        _notWater.Add(new NotWaterMark { X = Math.Round(w.X, 2), Z = Math.Round(w.Z, 2) });
        MarkingNotWater = false;
        SaveNotWater($"Marked not water in game. Re-surveying; rebuild the products to update them.");
    }

    /// <summary>Wet at the click or within 3 m of it, as the tracer reads a mark.</summary>
    static bool NearWater(GreensBook.WaterMap water, Vec2 w)
    {
        for (double dz = -3; dz <= 3; dz += 1)
            for (double dx = -3; dx <= 3; dx += 1)
                if (dx * dx + dz * dz <= 9 && water.IsWet(w + new Vec2(dx, dz))) return true;
        return false;
    }

    void RemoveNotWater(NotWaterMark mark)
    {
        _notWater.Remove(mark);
        SaveNotWater("Mark removed: that water comes back. Re-surveying; rebuild the products to update them.");
    }

    void SaveNotWater(string status)
    {
        try { NotWaterMarks.Save(OutputFolder, _notWater); }
        catch (Exception ex) { Status = $"Not saved: {ex.Message}"; return; }
        RebuildNotWaterRows();
        Resurvey();
        Status = status;
    }
}

public partial class NotWaterRowViewModel(string label, Action remove) : ObservableObject
{
    public string Label { get; } = label;
    [RelayCommand] private void Remove() => remove();
}
