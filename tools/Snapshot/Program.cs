using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using FfxiMacros.App.ViewModels;
using FfxiMacros.App.Views;
using FfxiMacros.Core.Settings;

namespace FfxiMacros.Tools.Snapshot;

/// <summary>
/// Renders the editor's window to a PNG, without a screen.
/// </summary>
/// <remarks>
/// Written for one reason: work on how the window looks can then be looked at. Avalonia's headless
/// platform normally stubs the drawing out; asked for real drawing it renders through Skia into a
/// bitmap, which is the same painting the screen would get.
///
///   dotnet run --project tools/Snapshot -- <folder> [width height]
///
/// The sample macro files in tests/FfxiMacros.Tests/Samples are copied into a throwaway USER tree,
/// so the window is full of macros rather than showing an empty pane.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "snapshots";
        int width = args.Length > 2 && int.TryParse(args[1], out int w) ? w : 1620;
        int height = args.Length > 2 && int.TryParse(args[2], out int h) ? h : 900;

        Directory.CreateDirectory(output);
        string user = BuildSampleFolder();

        try
        {
            AppBuilder.Configure<FfxiMacros.App.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            Dispatcher.UIThread.Invoke(() => Render(user, output, width, height));
            Console.WriteLine($"written to {Path.GetFullPath(output)}");
            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(user)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Render(string user, string output, int width, int height)
    {
        // The labels come from a resource dictionary the application installs at startup. Without
        // it every button renders blank, and the picture would be of something nobody ever sees.
        FfxiMacros.App.Localization.Loc.Apply(FfxiMacros.App.Localization.Language.English);

        var settings = new EditorSettings
        {
            UserFolder = user,
            BackupBeforeSave = false,
            SourcePath = Path.Combine(Path.GetTempPath(), "ffxi-snapshot-settings.json"),
        };

        var viewModel = new MainWindowViewModel(settings)
        {
            ProbeRunningClients = () => [],
            LiveStateFolder = Path.Combine(Path.GetTempPath(), "ffxi-snapshot-live"),
        };
        viewModel.Initialize();

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = width,
            Height = height,
        };

        window.Show();
        Settle();
        Save(window, Path.Combine(output, "window.png"));

        // The macro editor filled in, which is where most of the reading happens.
        viewModel.SelectedMacro = viewModel.CurrentSet?.Macros.FirstOrDefault(m => !m.IsEmpty);
        Settle();
        Save(window, Path.Combine(output, "macro-selected.png"));

        // What a tree row is actually made of, so its parts can be styled by name rather than by hope.
        if (window.GetVisualDescendants().OfType<TreeViewItem>().Skip(1).FirstOrDefault() is { } sample)
        {
            Console.WriteLine("  tree row parts:");
            foreach (var part in sample.GetVisualDescendants().OfType<Control>().Take(10))
            {
                Console.WriteLine($"    {part.GetType().Name}"
                                  + $"{(string.IsNullOrEmpty(part.Name) ? "" : $" #{part.Name}")}"
                                  + $"  {part.Bounds.Width:F0}x{part.Bounds.Height:F0}");
            }
        }

        // The pointer parked on a book row: hover is a state, and a state has to be looked at too.
        var rows = window.GetVisualDescendants().OfType<TreeViewItem>().ToList();
        if (rows.Count > 4)
        {
            var row = rows[4];
            var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
            if (centre is { } point)
            {
                window.MouseMove(point);
                Settle();
                Save(window, Path.Combine(output, "hover-book.png"));
            }
        }

        // The confirmation banner, which only shows when something is waiting to be answered.
        if (viewModel.Characters.OfType<CharacterNodeViewModel>().FirstOrDefault() is { } character)
        {
            viewModel.RequestBookClear(character.Books.First(b => b.Info.Number == 1));
            Settle();
            Save(window, Path.Combine(output, "notice-clear.png"));
            viewModel.CancelBookOperationCommand.Execute(null);
            Settle();

            // And the one the user actually meets: copy a book, paste it onto another.
            viewModel.CopyBookToClipboard(character.Books.First(b => b.Info.Number == 1));
            viewModel.PasteBookFromClipboard(character.Books.First(b => b.Info.Number == 9));
            Settle();
            Console.WriteLine($"    question: '{viewModel.PendingBookOperation?.Question}'");
            Console.WriteLine($"    confirm enabled: {viewModel.ConfirmBookOperationCommand.CanExecute(null)}"
                              + $", needs save: {viewModel.PendingBookOperation?.NeedsSave}");
            Save(window, Path.Combine(output, "notice-paste.png"));

            // The same banner in French, where the sentence is built from different pieces.
            viewModel.SetLanguageCommand.Execute("fr");
            Settle();
            Console.WriteLine($"    question (fr): '{viewModel.PendingBookOperation?.Question}'");
            Save(window, Path.Combine(output, "notice-paste-fr.png"));

            viewModel.SetLanguageCommand.Execute("en");
            viewModel.CancelBookOperationCommand.Execute(null);
            Settle();
        }

        // And the phrase picker open, the widest the window ever gets.
        viewModel.TogglePhrasesCommand.Execute(null);
        Settle();
        Save(window, Path.Combine(output, "phrases.png"));
    }

    /// <summary>Lets layout, styles and the first frame settle before the picture is taken.</summary>
    private static void Settle()
    {
        for (int i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(40);
        }
    }

    private static void Save(Window window, string path)
    {
        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine($"no frame for {path}");
            return;
        }

        frame.Save(path);
        Console.WriteLine($"  {Path.GetFileName(path)}  {frame.PixelSize.Width}x{frame.PixelSize.Height}");
    }

    /// <summary>A throwaway USER tree holding the sample macro files, as the tests build one.</summary>
    private static string BuildSampleFolder()
    {
        string samples = Path.GetFullPath(Path.Combine("tests", "FfxiMacros.Tests", "Samples"));
        string root = Path.Combine(Path.GetTempPath(), $"ffxi-snapshot-{Guid.NewGuid():N}");
        string user = Path.Combine(root, "USER");
        string character = Path.Combine(user, "13cf4e4");
        Directory.CreateDirectory(character);

        File.Copy(Path.Combine(samples, "mcr.ttl"), Path.Combine(character, "mcr.ttl"));
        File.Copy(Path.Combine(samples, "mcr_2.ttl"), Path.Combine(character, "mcr_2.ttl"));

        // Book 1 gets two sets, book 15 one: enough for the tabs and the counters to have something
        // to say. The names come from the sample files, so nothing here is invented.
        File.Copy(Path.Combine(samples, "mcr.dat"), Path.Combine(character, "mcr.dat"));
        File.Copy(Path.Combine(samples, "mcr10.dat"), Path.Combine(character, "mcr1.dat"));
        File.Copy(Path.Combine(samples, "mcr140.dat"), Path.Combine(character, "mcr140.dat"));
        File.Copy(Path.Combine(samples, "mcr160.dat"), Path.Combine(character, "mcr160.dat"));

        return user;
    }
}
