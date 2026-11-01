using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The Logo card in Course settings: the course's logo set, a seal, mark and lockup each in colour, ink and light. A
/// chosen file is copied into the Logos folder beside the products (Logos.json lists them); each product picks the
/// version that suits its background.
/// </summary>
public partial class WorkspaceViewModel
{
    /// <summary>Row by row: seal, mark, lockup; each in colour, ink, light.</summary>
    public ObservableCollection<LogoSlotViewModel> LogoSlots { get; } = [];

    [ObservableProperty] private string _logoError = "";

    void InitLogos()
    {
        LogoSlots.Clear();
        var set = CourseLogosFile.Load(OutputFolder);
        foreach (var kind in Enum.GetValues<LogoKind>())
            foreach (var tone in Enum.GetValues<LogoTone>())
                LogoSlots.Add(new LogoSlotViewModel(kind, tone, set.File(kind, tone) is { } f ? CourseLogosFile.ImagePath(OutputFolder, f) : null, RemoveLogo));
    }

    /// <summary>Copies <paramref name="source"/> into the course's logo set as the slot's image.</summary>
    public void ImportLogo(LogoSlotViewModel slot, string source)
    {
        try
        {
            var set = CourseLogosFile.Import(OutputFolder, slot.Kind, slot.Tone, source);
            slot.Show(CourseLogosFile.ImagePath(OutputFolder, set.File(slot.Kind, slot.Tone)!));
            LogoError = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogoError = $"Couldn't copy {Path.GetFileName(source)}: {e.Message}";
        }
    }

    void RemoveLogo(LogoSlotViewModel slot)
    {
        try
        {
            CourseLogosFile.Remove(OutputFolder, slot.Kind, slot.Tone);
            slot.Show(null);
            LogoError = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogoError = $"Couldn't remove the {slot.Tip.ToLowerInvariant()}: {e.Message}";
        }
    }
}

/// <summary>One slot of the Logo card: its thumbnail on a swatch of the ground it is meant for.</summary>
public partial class LogoSlotViewModel : ObservableObject
{
    static readonly IBrush White = new SolidColorBrush(Color.Parse("#ffffff")), Paper = new SolidColorBrush(Color.Parse("#ebe0c2")),
        Dark = new SolidColorBrush(Color.Parse("#1f1d1a")), DarkText = new SolidColorBrush(Color.Parse("#6b6f68")), LightText = new SolidColorBrush(Color.Parse("#b8b2a6"));

    readonly Action<LogoSlotViewModel> _remove;

    public LogoSlotViewModel(LogoKind kind, LogoTone tone, string? path, Action<LogoSlotViewModel> remove)
    {
        Kind = kind; Tone = tone; _remove = remove;
        Show(path);
    }

    public LogoKind Kind { get; }
    public LogoTone Tone { get; }

    /// <summary>The swatch behind the thumbnail: white for colour, parchment for ink, dark for light.</summary>
    public IBrush Swatch => Tone switch { LogoTone.Ink => Paper, LogoTone.Light => Dark, _ => White };
    public IBrush PromptBrush => Tone == LogoTone.Light ? LightText : DarkText;
    public string Tip => $"{Kind} ({Tone.ToString().ToLowerInvariant()})";

    [ObservableProperty] private Bitmap? _thumbnail;
    public bool HasImage => Thumbnail is not null;
    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));

    /// <summary>Shows the image at <paramref name="path"/> (read into memory, so the file stays free), or the empty slot.</summary>
    public void Show(string? path)
    {
        Bitmap? image = null;
        if (path is not null && File.Exists(path))
            try { image = Bitmap.DecodeToHeight(new MemoryStream(File.ReadAllBytes(path)), 160); }
            catch (Exception) { image = null; }
        Thumbnail = image;
    }

    [RelayCommand]
    void Remove() => _remove(this);
}
