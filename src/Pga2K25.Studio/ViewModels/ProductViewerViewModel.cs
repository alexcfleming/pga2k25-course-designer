using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>A built product's pages: thumbnails down the side, the selected page large, and open / rebuild actions.</summary>
public partial class ProductViewerViewModel : ObservableObject
{
    /// <summary>Pages are shown at most this wide; the printed files are larger than any screen needs.</summary>
    const int MaxViewWidth = 3200;

    readonly Func<Task> _build;
    readonly List<string> _pdfs;
    int _loadVersion;

    public ProductDefinition Product { get; }
    public string Name => Product.Name;
    public ObservableCollection<PageItemViewModel> Pages { get; } = [];
    public bool HasPages => Pages.Count > 0;
    public bool NotBuilt => Pages.Count == 0;
    public bool HasPdf => _pdfs.Count > 0;
    public bool ManyPages => Pages.Count > 1;
    public string Summary { get; }

    [ObservableProperty] private PageItemViewModel? _selectedPage;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _loadingPage;
    /// <summary>Why an open didn't happen, shown in the toolbar; empty when all is well.</summary>
    [ObservableProperty] private string _message = "";

    public ProductViewerViewModel(ProductDefinition product, StudioSettings settings, string courseName, Func<Task> build)
    {
        Product = product;
        _build = build;
        var pages = Products.PagesOf(product, settings, courseName);
        _pdfs = Products.PdfsOf(product, settings, courseName);
        foreach (var p in pages) Pages.Add(new PageItemViewModel(p));
        Summary = pages.Count == 0 ? "Not built yet"
            : $"{pages.Count} page{(pages.Count == 1 ? "" : "s")} · built {File.GetLastWriteTime(pages[0].Path):d MMM HH:mm}" + (_pdfs.Count > 0 ? " · PDF" : "");
        Task.Run(LoadThumbnails);
        SelectedPage = Pages.FirstOrDefault();
    }

    void LoadThumbnails()
    {
        foreach (var page in Pages.ToList())
        {
            try
            {
                var thumb = Bitmap.DecodeToWidth(new MemoryStream(File.ReadAllBytes(page.Page.Path)), 180);
                Dispatcher.UIThread.Post(() => page.Thumbnail = thumb);
            }
            catch (Exception) { /* a page that won't decode just has no thumbnail */ }
        }
    }

    partial void OnSelectedPageChanged(PageItemViewModel? value)
    {
        if (value is null) return;
        int version = ++_loadVersion;
        LoadingPage = true;
        Task.Run(() =>
        {
            Bitmap? bmp = null;
            try
            {
                // Pages wider than the screen needs are decoded smaller; the rest load as they are.
                int native = PngWidth(value.Page.Path) ?? int.MaxValue;
                bmp = native <= MaxViewWidth
                    ? new Bitmap(value.Page.Path)
                    : Bitmap.DecodeToWidth(new MemoryStream(File.ReadAllBytes(value.Page.Path)), MaxViewWidth, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception) { bmp = null; }
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _loadVersion) { bmp?.Dispose(); return; }
                var old = Image;
                Image = bmp;
                old?.Dispose();
                LoadingPage = false;
            });
        });
    }

    /// <summary>Width from a PNG header, without decoding the image.</summary>
    static int? PngWidth(string path)
    {
        Span<byte> head = stackalloc byte[24];
        using var f = File.OpenRead(path);
        if (f.Read(head) < 24 || head[1] != (byte)'P' || head[2] != (byte)'N' || head[3] != (byte)'G') return null;
        return head[16] << 24 | head[17] << 16 | head[18] << 8 | head[19];
    }

    [RelayCommand] private void OpenPdf()
    {
        if (_pdfs.FirstOrDefault() is { } pdf) Message = ShellOpen.Open(pdf) ?? "";
    }

    [RelayCommand] private void OpenImage()
    {
        if (SelectedPage is { } p) Message = ShellOpen.Open(p.Page.Path) ?? "";
    }

    [RelayCommand] private void ShowInFolder()
    {
        if (SelectedPage is { } p) Message = ShellOpen.Reveal(p.Page.Path) ?? "";
    }

    [RelayCommand] private Task Build() => _build();
}

public partial class PageItemViewModel(ProductPage page) : ObservableObject
{
    public ProductPage Page { get; } = page;
    public string Title => Page.Title;
    [ObservableProperty] private Bitmap? _thumbnail;
}
