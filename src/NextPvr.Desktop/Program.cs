using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace NextPvr.Desktop;

internal static class Program
{
    [STAThread] public static void Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}
public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        var theme = new FluentTheme();
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Avalonia.Media.Color.Parse(NextPvr.Core.Presentation.Accent) };
        Styles.Add(theme);
        Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Button>())
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(NextPvr.Core.Presentation.Surface))), new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, 20d), new Setter(Avalonia.Layout.Layoutable.MinHeightProperty, 52d), new Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(18, 10)) }
        });
        Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Button>().Class(":focus"))
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, Avalonia.Media.Brushes.Cyan), new Setter(Avalonia.Controls.Primitives.TemplatedControl.BorderThicknessProperty, new Thickness(3)) }
        });
        Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.ListBoxItem>().Class(":selected"))
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(NextPvr.Core.Presentation.Accent))) }
        });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
