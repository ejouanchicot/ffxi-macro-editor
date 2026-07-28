using Avalonia;

namespace FfxiMacros.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.DebugLogging = args.Contains("--debug", StringComparer.OrdinalIgnoreCase);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Used by the Avalonia designer as well as by <see cref="Main"/>.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
