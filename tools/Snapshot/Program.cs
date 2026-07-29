using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;
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
        Console.WriteLine($"  render scaling: {window.RenderScaling}");
        Save(window, Path.Combine(output, "window.png"));

        // The gesture itself, caught mid-flight: press on a book, move far enough to lift it, and
        // photograph the window while the card is in hand. Nothing else shows what is carried, and
        // « what I grab is not whole » is a sentence no amount of reading the code answers.
        var cards = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.DataContext is BookNodeViewModel && b.Classes.Contains("card"))
            .ToList();

        Console.WriteLine($"  {cards.Count} card(s) realised; "
                          + string.Join(", ", cards.Take(3).Select(c => $"{c.Bounds}")));

        DragAndPhotograph(window, output, "drag-book.png", cards.FirstOrDefault());

        // The realistic case: a book well down the list, where the row sits far into a scrolled
        // panel and its own offset is no longer a couple of pixels.
        DragAndPhotograph(window, output, "drag-book-low.png", cards.Skip(11).FirstOrDefault());

        DragAndPhotograph(window, output, "drag-macro.png",
                          window.GetVisualDescendants().OfType<Button>()
                              .FirstOrDefault(b => b.DataContext is MacroSlotViewModel));

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

        // What a dragged thing looks like while it travels: the picture taken of the card and of the
        // slot, on their own. A ghost cropped or shifted is only visible here.
        if (window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.DataContext is BookNodeViewModel && b.Classes.Contains("card")) is { } card)
        {
            SavePicture(card, Path.Combine(output, "ghost-book.png"));
        }

        if (window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.DataContext is MacroSlotViewModel) is { } slot)
        {
            SavePicture(slot, Path.Combine(output, "ghost-macro.png"));
        }

        // And the phrase picker open, the widest the window ever gets.
        viewModel.TogglePhrasesCommand.Execute(null);
        Settle();
        Save(window, Path.Combine(output, "phrases.png"));
    }

    /// <summary>Lifts something with the pointer and photographs the window while it is in hand.</summary>
    private static void DragAndPhotograph(Window window, string output, string name, Control? source)
    {
        if (source?.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), window)
            is not { } grab)
        {
            Console.Error.WriteLine($"  nothing to drag for {name}");
            return;
        }

        window.MouseDown(grab, MouseButton.Left);
        Pump();

        // Past the threshold first, then a real distance: the lift happens on the first move that
        // counts as one, and the picture wants the card well clear of the row it came from.
        foreach (var step in new[] { new Point(8, 12), new Point(30, 90), new Point(60, 170) })
        {
            window.MouseMove(grab + step, RawInputModifiers.LeftMouseButton);
            Pump();
        }

        Settle();
        Save(window, Path.Combine(output, name));

        window.MouseMove(grab, RawInputModifiers.LeftMouseButton);
        Pump();
        window.MouseUp(grab, MouseButton.Left);
        Settle();
    }

    private static void Pump()
    {
        for (int i = 0; i < 4; i++)
            Dispatcher.UIThread.RunJobs();
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

    /// <summary>
    /// Renders one control on its own, exactly as the window does when it is picked up.
    /// </summary>
    /// <remarks>
    /// Rendering a control renders it where it sits in its parent — its own Bounds position is part
    /// of the drawing, so the frame has to reach the far edge and the offset is cropped off after.
    /// This is the same arithmetic the window uses to draw a carried thing; getting it wrong is
    /// invisible on a screen at 100%, where every scale involved is one.
    /// </remarks>
    private static void SavePicture(Control source, string path)
    {
        var bounds = source.Bounds;
        var size = new PixelSize((int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
        Console.WriteLine($"  {Path.GetFileName(path)}  bounds {bounds}");

        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(source);
        bitmap.Save(path);
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
