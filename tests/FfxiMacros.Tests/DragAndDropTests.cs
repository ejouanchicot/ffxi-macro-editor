using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FfxiMacros.App.ViewModels;
using FfxiMacros.App.Views;
using FfxiMacros.Core.Settings;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// The drag of a macro, driven through a real window with a real pointer.
/// </summary>
/// <remarks>
/// It is drawn by the editor rather than by Windows — a picture under the cursor, the slot it left
/// hollowed out, the slot it would land on lit up — so none of it can be checked from a view model.
/// Two mistakes that shipped are pinned here: a hit test walked the logical tree, where a button's
/// own template parts do not lead back to it, and the canvas the picture is drawn on was reached
/// through a generated field this window never populates.
/// </remarks>
[Collection(nameof(HeadlessApplication))]
public class DragAndDropTests : IDisposable
{
    private readonly TempUserFolder _temp = new();

    public DragAndDropTests()
    {
        _temp.AddCharacter("aaaa1", 0, 1);
        _temp.AddTitles("aaaa1");
    }

    [Fact]
    public void DraggingAMacroOntoAnotherCarriesItAndSwapsThem() => InWindow((window, viewModel) =>
    {
        var layer = window.FindControl<Canvas>("DragLayer");
        Assert.NotNull(layer);

        var (from, onto) = TwoSlots(window);
        string carried = Slot(from).Name;
        string displaced = Slot(onto).Name;
        Assert.NotEqual(carried, displaced);

        window.MouseDown(Centre(window, from), MouseButton.Left);
        Pump();

        // Far enough to count as a drag rather than a shaky click.
        window.MouseMove(Centre(window, from) + new Point(40, 4), RawInputModifiers.LeftMouseButton);
        Pump();

        Assert.Single(layer.Children);                       // the macro is under the cursor
        Assert.Contains("dragging", from.Classes);           // and no longer in its slot

        window.MouseMove(Centre(window, onto), RawInputModifiers.LeftMouseButton);
        Pump();

        Assert.Contains("dropTarget", onto.Classes);         // where it would land, before letting go

        window.MouseUp(Centre(window, onto), MouseButton.Left);
        Settle();

        Assert.Equal(displaced, Slot(from).Name);
        Assert.Equal(carried, Slot(onto).Name);
        Assert.Empty(layer.Children);                        // nothing left drawn on the sheet
        Assert.DoesNotContain("dragging", from.Classes);
        Assert.DoesNotContain("dropTarget", onto.Classes);
    });

    [Fact]
    public void LettingGoOverNothingPutsTheMacroBack() => InWindow((window, viewModel) =>
    {
        var layer = window.FindControl<Canvas>("DragLayer")!;
        var (from, _) = TwoSlots(window);
        string before = Slot(from).Name;

        window.MouseDown(Centre(window, from), MouseButton.Left);
        Pump();
        window.MouseMove(Centre(window, from) + new Point(60, 0), RawInputModifiers.LeftMouseButton);
        Pump();
        window.MouseMove(new Point(window.Width - 20, window.Height - 20), RawInputModifiers.LeftMouseButton);
        Pump();

        window.MouseUp(new Point(window.Width - 20, window.Height - 20), MouseButton.Left);
        Settle();

        Assert.Equal(before, Slot(from).Name);
        Assert.Empty(layer.Children);
        Assert.DoesNotContain("dragging", from.Classes);
    });

    [Fact]
    public void HoldingControlCopiesInsteadOfSwapping() => InWindow((window, viewModel) =>
    {
        var (from, onto) = TwoSlots(window);
        string carried = Slot(from).Name;

        window.MouseDown(Centre(window, from), MouseButton.Left);
        Pump();
        window.MouseMove(Centre(window, from) + new Point(40, 4), RawInputModifiers.LeftMouseButton);
        Pump();
        window.MouseMove(Centre(window, onto), RawInputModifiers.LeftMouseButton);
        Pump();

        window.MouseUp(Centre(window, onto), MouseButton.Left, RawInputModifiers.Control);
        Settle();

        Assert.Equal(carried, Slot(from).Name);              // the source keeps its macro
        Assert.Equal(carried, Slot(onto).Name);
    });

    [Fact]
    public void DraggingABookOntoAnotherCarriesItAndSwapsThem() => InWindow((window, viewModel) =>
    {
        // A book used to travel through the system's drag and drop and arrive without ceremony,
        // then stop at a banner. It swaps now, so the ordinary drag goes through as directly as a
        // macro's — carried, lit where it would land, and applied when it gets there.
        var layer = window.FindControl<Canvas>("DragLayer")!;
        var (from, onto) = TwoBooks(window);
        string carried = Book(from).Header;
        string displaced = Book(onto).Header;
        Assert.NotEqual(carried, displaced);

        window.MouseDown(Centre(window, from), MouseButton.Left);
        Pump();
        window.MouseMove(Centre(window, from) + new Point(4, 40), RawInputModifiers.LeftMouseButton);
        Pump();

        Assert.Single(layer.Children);                       // the card is under the cursor
        Assert.Contains("dragging", from.Classes);           // and its row is left hollow

        window.MouseMove(Centre(window, onto), RawInputModifiers.LeftMouseButton);
        Pump();

        Assert.Contains("dropTarget", onto.Classes);

        window.MouseUp(Centre(window, onto), MouseButton.Left);
        Settle();

        var books = BookRows(window);
        Assert.Equal(displaced, Book(books[0]).Header);
        Assert.Equal(carried, Book(books[3]).Header);
        Assert.Empty(layer.Children);
    });

    [Fact]
    public void ABookIsNotMovedWhenSomethingHasToBeAskedFirst() => InWindow((_, viewModel) =>
    {
        // An unsaved edit is thrown away by the reload a book move needs, so the move stops and
        // asks. The view leans on this answer before it animates anything: showing two cards trade
        // places and then snap back would promise something the editor has not done.
        viewModel.CurrentSet!.Macros[0].Name = "changed";
        var books = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.ToList();
        string before = books[0].Header;

        Assert.False(viewModel.CanTransferBookAtOnce(books[0], books[3]));

        viewModel.TransferBook(books[0], books[3], swap: true);

        Assert.NotNull(viewModel.PendingBookOperation);              // asked, not done
        Assert.Equal(before, books[0].Header);
    });

    // ---------------------------------------------------------------- the window under test

    private void InWindow(Action<MainWindow, MainWindowViewModel> scenario) => Dispatcher.UIThread.Invoke(() =>
    {
        var settings = new EditorSettings
        {
            UserFolder = _temp.UserFolder,
            BackupBeforeSave = false,
            SourcePath = Path.Combine(_temp.Root, "settings.json"),
        };

        var viewModel = new MainWindowViewModel(settings)
        {
            ProbeRunningClients = () => [],
            LiveStateFolder = Path.Combine(_temp.Root, "live"),
        };
        viewModel.Initialize();

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Pump();

        try
        {
            scenario(window, viewModel);
        }
        finally
        {
            window.Close();
        }
    });

    private static (Button From, Button Onto) TwoSlots(MainWindow window)
    {
        var slots = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext is MacroSlotViewModel)
            .ToList();

        Assert.True(slots.Count >= 4, "the palette should offer its twenty slots");
        return (slots[0], slots[3]);
    }

    private static MacroSlotViewModel Slot(Button button) => (MacroSlotViewModel)button.DataContext!;

    /// <summary>The book cards, in the order they are listed.</summary>
    private static List<Border> BookRows(MainWindow window) =>
        [.. window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.DataContext is BookNodeViewModel && b.Classes.Contains("card"))];

    private static (Border From, Border Onto) TwoBooks(MainWindow window)
    {
        var rows = BookRows(window);
        Assert.True(rows.Count >= 4, "the character should list its books");
        return (rows[0], rows[3]);
    }

    private static BookNodeViewModel Book(Border row) => (BookNodeViewModel)row.DataContext!;

    private static Point Centre(MainWindow window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? default;

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>Waits out the swap, which lands when the two macros finish sliding past each other.</summary>
    private static void Settle()
    {
        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(30);
            Dispatcher.UIThread.RunJobs();
        }
    }

    public void Dispose() => _temp.Dispose();
}
