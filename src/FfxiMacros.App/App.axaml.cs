using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FfxiMacros.App.Localization;
using FfxiMacros.App.ViewModels;
using FfxiMacros.App.Views;
using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.GameData;
using FfxiMacros.Core.Settings;
using FfxiMacros.Core.Text;

namespace FfxiMacros.App;

public partial class App : Application
{
    /// <summary>Set from the command line: <c>--debug</c> turns on the log file.</summary>
    public static bool DebugLogging { get; set; }

    private FileLog? _log;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = SettingsStore.Load();

            // Before anything is built: the labels resolve against the merged dictionary this
            // installs, so switching afterwards only has to swap it again.
            Loc.Apply(Loc.Parse(settings.Language));

            IMacroLog? log = null;
            if (DebugLogging || settings.AlwaysLog)
            {
                _log = new FileLog(SettingsStore.DefaultLogPath);
                log = _log;
                desktop.Exit += (_, _) => _log?.Dispose();
            }

            // Readable auto-translate names: the game's own data files first, Windower only to
            // fill in what the client stores as markers.
            FfxiText.DefaultAutoTranslate = AutoTranslateDictionary.AutoLoad(
                FfxiDatIndex.InstallRootFor(settings.UserFolder), settings.WindowerFolder, log);

            var viewModel = new MainWindowViewModel(settings, log);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            viewModel.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
