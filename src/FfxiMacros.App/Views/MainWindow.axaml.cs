using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FfxiMacros.App.Localization;
using FfxiMacros.App.ViewModels;

namespace FfxiMacros.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Key under which a dragged macro slot or book travels in the drag payload.</summary>
    private const string DragFormat = "ffxi-macro-node";

    private object? _dragged;
    private Point _pressPosition;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;

        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
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

    /// <summary>Remembers what is under the cursor, so a drag can start once it actually moves.</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragged = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _pressPosition = e.GetPosition(this);
        _dragged = (e.Source as Control)?.DataContext switch
        {
            MacroSlotViewModel slot => slot,
            BookNodeViewModel book => book,
            _ => null,
        };
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Enough slop that a click with a shaky hand is never mistaken for a drag: dropping a
        // macro swaps two slots, which is not something to trigger by accident.
        Point position = e.GetPosition(this);
        if (Math.Abs(position.X - _pressPosition.X) < 14 && Math.Abs(position.Y - _pressPosition.Y) < 14)
            return;

        object payload = _dragged;
        _dragged = null;

        var data = new DataObject();
        data.Set(DragFormat, payload);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move | DragDropEffects.Copy);
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
                viewModel.RequestBookTransfer(from, to, move: !copy);
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

    // ---------------------------------------------------------------- context menu

    private void OnCopyMacro(object? sender, RoutedEventArgs e) =>
        ViewModel?.CopyMacroToClipboard(SlotOf(sender));

    private void OnPasteMacro(object? sender, RoutedEventArgs e) =>
        ViewModel?.PasteMacroFromClipboard(SlotOf(sender));

    private static MacroSlotViewModel? SlotOf(object? sender) =>
        (sender as MenuItem)?.DataContext as MacroSlotViewModel;

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
