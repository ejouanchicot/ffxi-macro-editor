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

    private static Point Centre(MainWindow window, Button button) =>
        button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window) ?? default;

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
