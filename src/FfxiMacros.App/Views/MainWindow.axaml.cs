using System.ComponentModel;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FfxiMacros.App.Localization;
using FfxiMacros.App.ViewModels;

namespace FfxiMacros.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Key under which a dragged macro slot or book travels in the drag payload.</summary>
    private const string DragFormat = "ffxi-macro-node";

    private object? _dragged;
    private object? _pressedNode;
    private Point _pressPosition;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;

        AddHandler(KeyDownEvent, OnClipboardKey, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.PickFolderAsync = PickFolderAsync;
        viewModel.SaveFileAsync = SaveFileAsync;
        viewModel.OpenFileAsync = OpenFileAsync;
        viewModel.InsertIntoFocusedField = InsertIntoFocusedField;
    }

    // ---------------------------------------------------------------- window chrome

    /// <summary>
    /// Drags the window by its header. The system title bar is hidden so the app can own that
    /// strip, which also means the app has to provide the drag itself.
    /// </summary>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsInteractive(e.Source as Control))
            return;

        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    /// <summary>True when the pointer is on something clickable rather than on the bar itself.</summary>
    private static bool IsInteractive(Control? source)
    {
        for (var control = source; control is not null; control = control.Parent as Control)
        {
            if (control is Button or TextBox or CheckBox)
                return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- phrase insertion

    /// <summary>
    /// The macro field the caret was last in. Kept because clicking a phrase in the picker moves
    /// the focus away from it, and the text still has to land where the user was typing.
    /// </summary>
    private TextBox? _lastMacroField;

    private void OnMacroFieldFocused(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox box)
            _lastMacroField = box;
    }

    /// <summary>Inserts a phrase at the caret of the field last edited, replacing any selection.</summary>
    private bool InsertIntoFocusedField(string text)
    {
        if (_lastMacroField is not { } box)
            return false;

        string current = box.Text ?? "";
        int start = Math.Clamp(box.SelectionStart, 0, current.Length);
        int end = Math.Clamp(box.SelectionEnd, 0, current.Length);
        if (start > end)
            (start, end) = (end, start);

        box.Text = current[..start] + text + current[end..];
        box.CaretIndex = start + text.Length;
        box.Focus();
        return true;
    }

    // ---------------------------------------------------------------- drag and drop

    // ---------------------------------------------------------------- carrying a macro

    /// <summary>
    /// A macro being carried: the slot it left, the picture under the cursor, and where it was
    /// grabbed inside that slot so it does not jump to the cursor's tip.
    /// </summary>
    /// <remarks>
    /// Windows' own drag and drop carries data, not a picture: the pointer changes shape and
    /// nothing else moves, so the macro appeared to teleport on release. This draws the whole
    /// gesture instead — the slot empties, the macro follows the cursor, the slot it would land on
    /// lights up, and on release the two swap by sliding past each other.
    /// </remarks>
    private Button? _carriedFrom;

    /// <summary>
    /// The sheet a carried macro is drawn on, looked up rather than taken from the generated field.
    /// </summary>
    /// <remarks>
    /// This window loads its XAML itself, so the fields the compiler generates for named controls
    /// are never assigned — reaching for one gives null, and the failure lands in an async handler
    /// where it goes unnoticed. Asked for by name instead, once, the first time a macro is lifted.
    /// </remarks>
    private Canvas? _dragLayer;

    private Canvas? Layer => _dragLayer ??= this.FindControl<Canvas>("DragLayer");

    private Border? _carried;
    private Point _grabOffset;
    private Button? _dropTarget;

    /// <summary>Long enough to read as a movement, short enough not to feel like a delay.</summary>
    private static readonly TimeSpan SwapDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>The eased slide the two macros make as they trade places.</summary>
    private static Transitions Sliding() =>
    [
        new DoubleTransition { Property = Canvas.LeftProperty, Duration = SwapDuration, Easing = new CubicEaseOut() },
        new DoubleTransition { Property = Canvas.TopProperty, Duration = SwapDuration, Easing = new CubicEaseOut() },
    ];

    private bool IsCarrying => _carried is not null;

    /// <summary>Lifts a macro out of its slot and puts it under the cursor.</summary>
    private void StartCarrying(Button slot, Point pointer)
    {
        if (Layer is not { } layer || slot.TranslatePoint(default, layer) is not { } origin)
            return;

        _carriedFrom = slot;
        _grabOffset = pointer - origin;

        _carried = new Border
        {
            Classes = { "ghost" },
            Width = slot.Bounds.Width,
            Height = slot.Bounds.Height,
            Child = new TextBlock
            {
                Text = (slot.DataContext as MacroSlotViewModel)?.DisplayName ?? "",
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            },

            // No transitions while it is being carried. They belong to the landing, and left on
            // here they smooth every single mouse move: the macro then trails the cursor by a
            // sixth of a second, which reads as the window struggling to keep up.
        };

        MoveCarried(pointer);
        layer.Children.Add(_carried);
        slot.Classes.Add("dragging");
    }

    private void MoveCarried(Point pointer)
    {
        if (_carried is null)
            return;

        Canvas.SetLeft(_carried, pointer.X - _grabOffset.X);
        Canvas.SetTop(_carried, pointer.Y - _grabOffset.Y);
    }

    /// <summary>Lights up the slot the macro would land on, and only that one.</summary>
    private void HighlightDropTarget(Point pointer)
    {
        var slot = SlotButtonAt(pointer);
        if (ReferenceEquals(slot, _dropTarget))
            return;

        _dropTarget?.Classes.Remove("dropTarget");
        _dropTarget = ReferenceEquals(slot, _carriedFrom) ? null : slot;
        _dropTarget?.Classes.Add("dropTarget");
    }

    /// <summary>
    /// The macro slot button under a point, or null.
    /// </summary>
    /// <remarks>
    /// Up the visual tree, not the logical one. A hit test lands on whatever is drawn there, which
    /// inside a button is a piece of its template — and a template part has no logical parent
    /// leading back to the button it belongs to.
    /// </remarks>
    private Button? SlotButtonAt(Point point)
    {
        for (var visual = this.InputHitTest(point) as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button { DataContext: MacroSlotViewModel } button)
                return button;
        }

        return null;
    }

    /// <summary>
    /// Puts the macro down: the two slots slide past each other, and the swap lands as they arrive.
    /// </summary>
    private async void DropCarried(bool copy)
    {
        var carried = _carried;
        var from = _carriedFrom;
        var onto = _dropTarget;

        _carried = null;
        _carriedFrom = null;
        _dropTarget = null;
        onto?.Classes.Remove("dropTarget");

        if (carried is null || from is null || Layer is not { } layer)
            return;

        var source = from.DataContext as MacroSlotViewModel;
        var target = onto?.DataContext as MacroSlotViewModel;

        // The slide is given to it now, for the one movement it has left to make.
        carried.Transitions = Sliding();

        // Nowhere to land: it goes back where it came from rather than vanishing.
        Border? returning = target is not null && !copy ? Lift(onto!, from) : null;

        if ((onto ?? from).TranslatePoint(default, layer) is { } landing)
        {
            Canvas.SetLeft(carried, landing.X);
            Canvas.SetTop(carried, landing.Y);
        }

        await Task.Delay(SwapDuration);

        layer.Children.Remove(carried);
        if (returning is not null)
            layer.Children.Remove(returning);

        from.Classes.Remove("dragging");
        onto?.Classes.Remove("dragging");

        if (source is not null && target is not null)
            ViewModel?.TransferMacro(source, target, copy);
    }

    /// <summary>
    /// The macro being displaced, drawn sliding the other way.
    /// </summary>
    /// <remarks>
    /// A swap is two movements. Showing only the one under the cursor would leave the other slot
    /// changing without explanation, which is the very thing that made the old behaviour abrupt.
    /// </remarks>
    private Border? Lift(Button slot, Button towards)
    {
        if (Layer is not { } layer
            || slot.TranslatePoint(default, layer) is not { } start
            || towards.TranslatePoint(default, layer) is not { } end)
        {
            return null;
        }

        var moving = new Border
        {
            Classes = { "ghost" },
            Width = slot.Bounds.Width,
            Height = slot.Bounds.Height,
            Opacity = 0.75,
            Child = new TextBlock
            {
                Text = (slot.DataContext as MacroSlotViewModel)?.DisplayName ?? "",
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Transitions = Sliding(),
        };

        Canvas.SetLeft(moving, start.X);
        Canvas.SetTop(moving, start.Y);
        layer.Children.Add(moving);
        slot.Classes.Add("dragging");

        Canvas.SetLeft(moving, end.X);
        Canvas.SetTop(moving, end.Y);
        return moving;
    }

    /// <summary>Remembers what is under the cursor, so a drag can start once it actually moves.</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Every press, left or right: the right button is what opens a context menu, and the menu
        // acts on whatever was under the cursor at that moment.
        _pressedNode = NodeUnder(e.Source as Control);

        _dragged = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _pressPosition = e.GetPosition(this);

        // Only a macro and a book can be dragged: a set is a tab, and a character is a folder.
        _dragged = _pressedNode is MacroSlotViewModel or BookNodeViewModel ? _pressedNode : null;
    }

    /// <summary>The macro, set, book or character a control belongs to.</summary>
    private static object? NodeUnder(Control? source)
    {
        for (var control = source; control is not null; control = control.Parent as Control)
        {
            if (control.DataContext is MacroSlotViewModel or SetNodeViewModel or TreeNodeViewModel)
                return control.DataContext;
        }

        return null;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point position = e.GetPosition(this);

        // A macro already in hand simply follows, and says where it would land.
        if (IsCarrying)
        {
            MoveCarried(position);
            HighlightDropTarget(position);
            return;
        }


        if (_dragged is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Enough slop that a click with a shaky hand is never mistaken for a drag, and no more:
        // Windows itself lifts at four pixels, and every one beyond that is felt as the macro
        // refusing to leave its slot.
        if (Math.Abs(position.X - _pressPosition.X) < 5 && Math.Abs(position.Y - _pressPosition.Y) < 5)
            return;

        // A macro is carried by the editor, which can draw it; a book still travels through
        // Windows' drag and drop, since it is dropped on a tree far from where it was picked up.
        if (_dragged is MacroSlotViewModel && SlotButtonAt(_pressPosition) is { } slot)
        {
            _dragged = null;
            e.Pointer.Capture(this);
            StartCarrying(slot, position);
            return;
        }

        object payload = _dragged;
        _dragged = null;

        var data = new DataObject();
        data.Set(DragFormat, payload);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    /// <summary>Puts down whatever the pointer was carrying.</summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsCarrying)
            return;

        e.Pointer.Capture(null);
        e.Handled = true;
        DropCarried(e.KeyModifiers.HasFlag(KeyModifiers.Control));
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = Target(e) is null
            ? DragDropEffects.None
            : e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move;

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        object? source = e.Data.Get(DragFormat);
        object? target = Target(e);
        e.Handled = true;

        if (ViewModel is not { } viewModel || source is null || target is null || ReferenceEquals(source, target))
            return;

        bool copy = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (source, target)
        {
            // Inside the palette: Ctrl copies, a plain drag swaps the two slots.
            case (MacroSlotViewModel from, MacroSlotViewModel to):
                viewModel.TransferMacro(from, to, copy);
                break;

            // Books overwrite ten files at once, so the drop only proposes the operation.
            case (BookNodeViewModel from, BookNodeViewModel to):
                viewModel.RequestBookTransfer(from, to, swap: !copy);
                break;
        }
    }

    /// <summary>The macro slot or book under the cursor during a drag.</summary>
    private static object? Target(DragEventArgs e)
    {
        for (var control = e.Source as Control; control is not null; control = control.Parent as Control)
        {
            if (control.DataContext is MacroSlotViewModel or BookNodeViewModel)
                return control.DataContext;
        }

        return null;
    }

    // ---------------------------------------------------------------- copy and paste

    /// <summary>
    /// Ctrl+C and Ctrl+V on whatever the keyboard is on: a macro slot, a set tab or a book.
    /// </summary>
    /// <remarks>
    /// A text box handles both gestures itself and marks the event handled, so typing in a macro
    /// line still copies text rather than the macro around it — this only ever sees the keys the
    /// fields did not want.
    /// </remarks>
    private void OnClipboardKey(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None)
        {
            viewModel.BeginRename(FocusedNode() as TreeNodeViewModel);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Control)
            return;

        switch (e.Key)
        {
            case Key.C:
                viewModel.CopyToClipboard(FocusedNode());
                e.Handled = true;
                break;

            case Key.V:
                viewModel.PasteFromClipboard(FocusedNode());
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The macro, set or book the keyboard is on. Falls back to the macro being edited, so Ctrl+C
    /// from a toolbar button or the window itself still copies what the user is looking at.
    /// </summary>
    private object? FocusedNode()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;

        for (var control = focused; control is not null; control = control.Parent as Control)
        {
            if (control.DataContext is MacroSlotViewModel or SetNodeViewModel or BookNodeViewModel)
                return control.DataContext;
        }

        return ViewModel?.SelectedMacro;
    }

    private void OnCopyMacro(object? sender, RoutedEventArgs e) =>
        ViewModel?.CopyMacroToClipboard(NodeOf<MacroSlotViewModel>(sender));

    private void OnPasteMacro(object? sender, RoutedEventArgs e) =>
        ViewModel?.PasteMacroFromClipboard(NodeOf<MacroSlotViewModel>(sender));

    private void OnCopySet(object? sender, RoutedEventArgs e) =>
        ViewModel?.CopySetToClipboard(NodeOf<SetNodeViewModel>(sender));

    private void OnPasteSet(object? sender, RoutedEventArgs e) =>
        ViewModel?.PasteSetFromClipboard(NodeOf<SetNodeViewModel>(sender));

    // ---------------------------------------------------------------- renaming a book

    private void OnRenameNode(object? sender, RoutedEventArgs e) =>
        ViewModel?.BeginRename(NodeOf<TreeNodeViewModel>(sender));

    /// <summary>
    /// Puts the caret in the box, all of it selected, the moment the row swaps its label for one.
    /// </summary>
    /// <remarks>
    /// The box is built with the row and merely hidden, so being attached to the tree says nothing
    /// about a rename starting — that was the whole bug: the focus was asked for once, at load, for
    /// every row at once, and never again. What matters is the moment it becomes visible. Selecting
    /// the whole name means typing replaces it, which is what renaming usually means.
    /// </remarks>
    private static void OnRenameBoxShown(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        box.PropertyChanged -= OnRenameBoxProperty;
        box.PropertyChanged += OnRenameBoxProperty;

        if (box.IsVisible)
            TakeTheCaret(box);
    }

    private static void OnRenameBoxProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && sender is TextBox { IsVisible: true } box)
            TakeTheCaret(box);
    }

    /// <summary>
    /// Waits for the layout pass before reaching for the focus.
    /// </summary>
    /// <remarks>
    /// A control that has just been made visible has not been arranged yet, and focusing something
    /// of no size is quietly ignored.
    /// </remarks>
    private static void TakeTheCaret(TextBox box) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                box.Focus();
                box.SelectAll();
            },
            DispatcherPriority.Loaded);

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not TreeNodeViewModel node)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                ViewModel?.CommitRename(node);
                break;

            case Key.Escape:
                e.Handled = true;
                ViewModel?.CancelRename(node);
                break;
        }
    }

    /// <summary>Clicking away commits, the way renaming a file does everywhere else.</summary>
    private void OnRenameCommitted(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TreeNodeViewModel node })
            ViewModel?.CommitRename(node);
    }

    private void OnClearSet(object? sender, RoutedEventArgs e) =>
        ViewModel?.ClearSet(NodeOf<SetNodeViewModel>(sender));

    private void OnClearBook(object? sender, RoutedEventArgs e) =>
        ViewModel?.RequestBookClear(NodeOf<BookNodeViewModel>(sender));

    private void OnCopyBook(object? sender, RoutedEventArgs e) =>
        ViewModel?.CopyBookToClipboard(NodeOf<BookNodeViewModel>(sender));

    private void OnPasteBook(object? sender, RoutedEventArgs e) =>
        ViewModel?.PasteBookFromClipboard(NodeOf<BookNodeViewModel>(sender));

    /// <summary>
    /// What a menu entry acts on: the node the right-click landed on, falling back to the data the
    /// menu inherited from the control it hangs off.
    /// </summary>
    private T? NodeOf<T>(object? sender) where T : class =>
        _pressedNode as T ?? (sender as MenuItem)?.DataContext as T;

    private void OnMacroMenuOpening(object? sender, CancelEventArgs e) =>
        EnablePaste(sender, viewModel => viewModel.CanPasteMacro);

    private void OnSetMenuOpening(object? sender, CancelEventArgs e) =>
        EnablePaste(sender, viewModel => viewModel.CanPasteSet);

    /// <summary>
    /// The tree lists characters as well as books and both share one row template, so the menu
    /// shows only the entries that mean something for the row it was opened on.
    /// </summary>
    private void OnTreeMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        bool book = _pressedNode is BookNodeViewModel || menu.DataContext is BookNodeViewModel;

        foreach (var item in menu.Items.OfType<Control>())
        {
            if (Equals(item.Tag, "character"))
                item.IsVisible = !book;
            else if (Equals(item.Tag, "book") || Equals(item.Tag, "paste"))
                item.IsVisible = book;
        }

        EnablePaste(sender, viewModel => viewModel.CanPasteBook);
    }

    /// <summary>
    /// Greys out the « Paste » entry when its clipboard is empty.
    /// </summary>
    /// <remarks>
    /// Done as the menu opens rather than by a binding: a context menu lives in a popup of its own,
    /// so a binding walking up to the window is one more thing that can quietly resolve to nothing.
    /// </remarks>
    private void EnablePaste(object? sender, Func<MainWindowViewModel, bool> canPaste)
    {
        if (sender is not ContextMenu menu || ViewModel is not { } viewModel)
            return;

        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => Equals(item.Tag, "paste")))
            item.IsEnabled = canPaste(viewModel);
    }

    // ---------------------------------------------------------------- file dialogs

    private async Task<string?> PickFolderAsync(string? startAt)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choisis le dossier USER de FFXI",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolder(startAt),
        });

        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }

    private async Task<string?> SaveFileAsync(string suggestedName, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter le set",
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = ExportFileTypes,
        });

        return file?.TryGetLocalPath();
    }

    private async Task<string?> OpenFileAsync(string extension)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importer un set",
            AllowMultiple = false,
            FileTypeFilter = ExportFileTypes,
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private static IReadOnlyList<FilePickerFileType> ExportFileTypes =>
    [
        new("Macros (texte)") { Patterns = ["*.txt"] },
        new("Macros (JSON)") { Patterns = ["*.json"] },
    ];

    private async Task<IStorageFolder?> TryGetFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- closing

    /// <summary>Keeps the window open the first time, rather than losing unsaved edits silently.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is not { } viewModel || viewModel.DirtyCount == 0 || _forceClose)
            return;

        e.Cancel = true;
        _forceClose = true;
        viewModel.SetStatus(Loc.T("Status.CloseWithChanges", viewModel.DirtySummary), error: true);
    }
}
