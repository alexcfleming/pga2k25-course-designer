using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Pga2K25.Studio.Views;

public partial class HoleSimView : UserControl
{
    // The .NET 8 SDK's Roslyn is too old for Avalonia 12's XAML source generator, so load by hand (as the other views do).
    public HoleSimView() => AvaloniaXamlLoader.Load(this);
}
