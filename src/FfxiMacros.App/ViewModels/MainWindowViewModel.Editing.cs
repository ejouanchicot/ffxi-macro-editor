using System.Collections.ObjectModel;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using FfxiMacros.Core.Operations;
using FfxiMacros.Core.Serialization;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

/// <summary>A search hit, ready to be listed and jumped to.</summary>
public sealed class SearchHitViewModel(MacroSearchHit hit)
{
    public MacroSearchHit Hit { get; } = hit;

    public string Location => Hit.Location;

    public string Text => Hit.Text;
}

/// <summary>What a pending book copy or move would do, waiting for the user to confirm it.</summary>
public sealed class PendingBookOperation(BookNodeViewModel source, BookNodeViewModel target, bool move)
{
    public BookNodeViewModel Source { get; } = source;

    public BookNodeViewModel Target { get; } = target;

    public bool Move { get; } = move;

    public string Question =>
        Loc.T(Move ? "Book.MoveQuestion" : "Book.CopyQuestion",
              Source.Info.Number, Source.Info.Title, Source.Parent.Character.Label,
              Target.Info.Number, Target.Info.Title, Target.Parent.Character.Label)
        + Loc.T("Book.Overwrites", Target.Info.SetCount)
        + Loc.T(Move ? "Book.SourceEmptied" : "Book.End");
}

public sealed partial class MainWindowViewModel
{
    private Macro? _macroClipboard;
    private string _searchQuery = "";
    private SearchHitViewModel? _selectedSearchResult;
    private PendingBookOperation? _pendingBookOperation;
    private bool _searchPanelOpen;

    // ---------------------------------------------------------------- file dialogs, supplied by the view

    /// <summary>Asks the view for a path to write to. Parameters: suggested file name, extension.</summary>
    public Func<string, string, Task<string?>>? SaveFileAsync { get; set; }

    /// <summary>Asks the view for a file to read. Parameter: extension.</summary>
    public Func<string, Task<string?>>? OpenFileAsync { get; set; }

    // ---------------------------------------------------------------- the running game

    private IReadOnlyList<string> _runningClients = [];

    /// <summary>
    /// Which clients are running. Replaceable so tests do not depend on what is on the machine.
    /// </summary>
    public Func<IReadOnlyList<string>> ProbeRunningClients { get; set; } = FfxiProcess.LoggedInCharacters;

    public bool IsGameRunning => _runningClients.Count > 0;

    /// <summary>
    /// Warning shown while a client might still own the macro files.
    /// </summary>
    /// <remarks>
    /// Measured on a live install: logging out to the character-select screen makes the client flush
    /// its macros to disk and let go of them, so a save 18 seconds later survived and showed up in
    /// game on the next login. Quitting is therefore not required — returning to character select is.
    /// </remarks>
    public string GameRunningWarning => Loc.T("Game.Connected", string.Join(", ", _runningClients));

    private RelayCommand? _recheckGameCommand;
    public RelayCommand RecheckGameCommand =>
        _recheckGameCommand ??= new RelayCommand(() =>
        {
            RefreshGameState();
            SetStatus(IsGameRunning ? GameRunningWarning : Loc.T("Game.NobodyConnected"), IsGameRunning);
        });

    /// <summary>Re-reads which clients are running, and forgets any override once they are gone.</summary>
    public void RefreshGameState()
    {
        _runningClients = ProbeRunningClients();
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(GameRunningWarning));
        OnPropertyChanged(nameof(ShowGameRunningBanner));
    }

    public bool ShowGameRunningBanner => IsGameRunning;

    /// <summary>
    /// Always allows the write, and reports what the player has to do for it to take effect.
    /// </summary>
    /// <remarks>
    /// Verified in game: the client reads a book's macros from disk the moment you switch to it,
    /// and only owns the one currently displayed. Refusing every save while logged in was therefore
    /// far too broad — the only losing case is editing the book that is on screen right now.
    /// </remarks>
    private bool MayWriteToDisk()
    {
        RefreshGameState();
        return true;
    }

    /// <summary>Told after a save, so the player knows how to make it show up.</summary>
    private string SaveAdvice(SetNodeViewModel set) =>
        IsGameRunning ? Loc.T("Game.SaveAdvice", set.Info.BookNumber) : "";

    // ---------------------------------------------------------------- macro clipboard

    public bool CanPasteMacro => _macroClipboard is not null;

    /// <summary>Name of the macro on the clipboard, for the menu label.</summary>
    public string ClipboardSummary =>
        _macroClipboard is null ? "" : $"presse-papier : {(_macroClipboard.IsEmpty ? "(vide)" : _macroClipboard.Name)}";

    public void CopyMacroToClipboard(MacroSlotViewModel? slot)
    {
        if (slot is null)
            return;

        _macroClipboard = slot.Macro.Clone();
        OnPropertyChanged(nameof(CanPasteMacro));
        OnPropertyChanged(nameof(ClipboardSummary));
        SetStatus($"{slot.SlotLabel} copié.");
    }

    public void PasteMacroFromClipboard(MacroSlotViewModel? slot)
    {
        if (slot is null || _macroClipboard is null)
            return;

        MacroOperations.CopyMacro(_macroClipboard, slot.Macro);
        slot.NotifyMacroReplaced();
        slot.OnLineEdited();
        SelectedMacro = slot;
        SetStatus($"Collé sur {slot.SlotLabel}.");
    }

    /// <summary>Drag and drop inside the palette: move by default, copy when asked.</summary>
    public void TransferMacro(MacroSlotViewModel source, MacroSlotViewModel target, bool copy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(source, target))
            return;

        if (copy)
            MacroOperations.CopyMacro(source.Macro, target.Macro);
        else
            MacroOperations.SwapMacros(source.Macro, target.Macro);

        source.NotifyMacroReplaced();
        target.NotifyMacroReplaced();
        source.OnLineEdited();
        target.OnLineEdited();
        SelectedMacro = target;

        SetStatus(copy
            ? $"{source.SlotLabel} copié sur {target.SlotLabel}."
            : $"{source.SlotLabel} et {target.SlotLabel} échangés.");
    }

    // ---------------------------------------------------------------- repair

    private RelayCommand? _repairCommand;
    public RelayCommand RepairCommand =>
        _repairCommand ??= new RelayCommand(RepairCurrentSet, () => _currentSet?.IsLoaded == true);

    private void RepairCurrentSet()
    {
        if (_currentSet?.Loaded is null)
            return;

        var suggestions = MacroRepair.Inspect(_currentSet.Loaded);
        if (suggestions.Count == 0)
        {
            SetStatus(Loc.T("Status.NothingToRepair", _currentSet.Info.FileName));
            return;
        }

        MacroRepair.Repair(_currentSet.Loaded);
        foreach (var slot in _currentSet.Macros)
            slot.NotifyMacroReplaced();
        _currentSet.MarkDirty();

        string first = suggestions[0].Where;
        SetStatus(suggestions.Count == 1
            ? Loc.T("Status.RepairedOne", first)
            : Loc.T("Status.RepairedMany", suggestions.Count, first));
    }

    // ---------------------------------------------------------------- import / export

    private AsyncRelayCommand? _exportSetCommand;
    public AsyncRelayCommand ExportSetCommand =>
        _exportSetCommand ??= new AsyncRelayCommand(ExportSetAsync, () => _currentSet?.IsLoaded == true);

    private AsyncRelayCommand? _importSetCommand;
    public AsyncRelayCommand ImportSetCommand =>
        _importSetCommand ??= new AsyncRelayCommand(ImportSetAsync, () => _currentSet?.IsLoaded == true);

    private async Task ExportSetAsync()
    {
        if (_currentSet?.Loaded is null || SaveFileAsync is null)
            return;

        var book = _currentSet.Parent.Info;
        string suggested = $"{_currentSet.Parent.Parent.Character.Id}-book{book.Number}-set{_currentSet.Info.SetNumber}";

        string? path = await SaveFileAsync(suggested + MacroTextFormat.FileExtension, MacroTextFormat.FileExtension);
        if (path is null)
            return;

        try
        {
            bool json = path.EndsWith(MacroJsonFormat.FileExtension, StringComparison.OrdinalIgnoreCase);
            string content = json
                ? MacroJsonFormat.Export(_currentSet.Loaded, _currentSet.Parent.Parent.Character.Id, book.Number,
                    book.Title, _currentSet.Info.SetNumber)
                : MacroTextFormat.Export(_currentSet.Loaded,
                    $"{_currentSet.Parent.Parent.Character.Label} · book {book.Number} ({book.Title}) · set {_currentSet.Info.SetNumber}");

            LongPath.WriteAllBytesAtomic(path, System.Text.Encoding.UTF8.GetBytes(content));
            SetStatus($"Exporté vers {path}.");
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.ToString(), error: true);
        }
    }

    private async Task ImportSetAsync()
    {
        if (_currentSet?.Loaded is null || OpenFileAsync is null)
            return;

        string? path = await OpenFileAsync(MacroTextFormat.FileExtension);
        if (path is null)
            return;

        try
        {
            string content = System.Text.Encoding.UTF8.GetString(LongPath.ReadAllBytes(path));
            bool json = path.EndsWith(MacroJsonFormat.FileExtension, StringComparison.OrdinalIgnoreCase);

            if (json)
                MacroJsonFormat.Import(content, _currentSet.Loaded);
            else
                MacroTextFormat.Import(content, _currentSet.Loaded);

            foreach (var slot in _currentSet.Macros)
                slot.NotifyMacroReplaced();
            _currentSet.MarkDirty();

            SetStatus($"Importé depuis {path}. Enregistre pour appliquer.");
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.ToString(), error: true);
        }
    }

    // ---------------------------------------------------------------- book copy / move

    public PendingBookOperation? PendingBookOperation
    {
        get => _pendingBookOperation;
        private set
        {
            if (SetField(ref _pendingBookOperation, value))
                OnPropertyChanged(nameof(HasPendingBookOperation));
        }
    }

    public bool HasPendingBookOperation => _pendingBookOperation is not null;

    private RelayCommand? _confirmBookOperationCommand;
    public RelayCommand ConfirmBookOperationCommand =>
        _confirmBookOperationCommand ??= new RelayCommand(ConfirmBookOperation, () => _pendingBookOperation is not null);

    private RelayCommand? _cancelBookOperationCommand;
    public RelayCommand CancelBookOperationCommand =>
        _cancelBookOperationCommand ??= new RelayCommand(() => { PendingBookOperation = null; SetStatus(Loc.T("Status.Cancelled")); });

    /// <summary>
    /// A dropped book is never applied straight away: copying a book overwrites ten files, so the
    /// user is shown exactly what will happen and has to confirm.
    /// </summary>
    public void RequestBookTransfer(BookNodeViewModel source, BookNodeViewModel target, bool move)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (ReferenceEquals(source, target))
            return;

        if (DirtySets.Any())
        {
            SetStatus(Loc.T("Status.SaveBeforeMove", DirtySummary), error: true);
            return;
        }

        PendingBookOperation = new PendingBookOperation(source, target, move);
        SetStatus(PendingBookOperation.Question);
    }

    private void ConfirmBookOperation()
    {
        if (_pendingBookOperation is not { } operation)
            return;

        try
        {
            if (!MayWriteToDisk())
                return;

            if (_settings.BackupBeforeSave)
            {
                BackupOnce(operation.Target.Parent.Character);
                if (operation.Move)
                    BackupOnce(operation.Source.Parent.Character);
            }

            if (operation.Move)
                MacroOperations.MoveBook(operation.Source.Info, operation.Target.Info, _log);
            else
                MacroOperations.CopyBook(operation.Source.Info, operation.Target.Info, keepTargetTitle: false, log: _log);

            SetStatus(Loc.T(operation.Move ? "Status.BookMoved" : "Status.BookCopied",
                            operation.Source.Info.Number, operation.Target.Info.Number));
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.ToString(), error: true);
        }
        finally
        {
            PendingBookOperation = null;
            ReloadAfterFileChange();
        }
    }

    /// <summary>Re-reads the folder from disk after files moved underneath us.</summary>
    private void ReloadAfterFileChange()
    {
        if (_library is null)
            return;

        int bookNumber = _currentBook?.Info.Number ?? 1;
        string? characterId = _currentBook?.Parent.Character.Id;

        OpenFolder(_library.UserFolder, remember: false);

        var character = Characters.OfType<CharacterNodeViewModel>()
            .FirstOrDefault(c => string.Equals(c.Character.Id, characterId, StringComparison.OrdinalIgnoreCase));
        var book = character?.Books.FirstOrDefault(b => b.Info.Number == bookNumber);
        if (book is not null)
            SelectedNode = book;
    }

    // ---------------------------------------------------------------- search

    public bool SearchPanelOpen
    {
        get => _searchPanelOpen;
        set => SetField(ref _searchPanelOpen, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetField(ref _searchQuery, value);
    }

    public ObservableCollection<SearchHitViewModel> SearchResults { get; } = [];

    public string SearchSummary { get; private set; } = "";

    public SearchHitViewModel? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (SetField(ref _selectedSearchResult, value) && value is not null)
                GoToHit(value.Hit);
        }
    }

    private RelayCommand? _searchCommand;
    public RelayCommand SearchCommand =>
        _searchCommand ??= new RelayCommand(RunSearch, () => _library is not null);

    private RelayCommand? _toggleSearchCommand;
    public RelayCommand ToggleSearchCommand =>
        _toggleSearchCommand ??= new RelayCommand(() => SearchPanelOpen = !SearchPanelOpen);

    private void RunSearch()
    {
        SearchResults.Clear();
        _selectedSearchResult = null;

        if (_library is null || string.IsNullOrWhiteSpace(_searchQuery))
        {
            SearchSummary = "";
            OnPropertyChanged(nameof(SearchSummary));
            return;
        }

        SearchPanelOpen = true;

        var hits = MacroSearch.Search(_library, _searchQuery, log: _log);
        foreach (var hit in hits)
            SearchResults.Add(new SearchHitViewModel(hit));

        SearchSummary = SearchSummaryFor(hits.Count);
        OnPropertyChanged(nameof(SearchSummary));
        SetStatus(SearchSummary);
    }

    /// <summary>How a result count reads; shared so a language switch can re-word it in place.</summary>
    private string SearchSummaryFor(int count) => count switch
    {
        0 => Loc.T("Search.NoResult", _searchQuery),
        1 => Loc.T("Search.OneResult"),
        _ => Loc.T("Search.ManyResults", count),
    };

    /// <summary>Opens the book, set and macro a search hit points at.</summary>
    private void GoToHit(MacroSearchHit hit)
    {
        var character = Characters.OfType<CharacterNodeViewModel>()
            .FirstOrDefault(c => string.Equals(c.Character.Id, hit.Character.Id, StringComparison.OrdinalIgnoreCase));
        var book = character?.Books.FirstOrDefault(b => b.Info.Number == hit.BookNumber);
        if (book is null)
            return;

        character!.IsExpanded = true;
        SelectedNode = book;

        if (hit.SetNumber >= 1)
            CurrentSet = book.Sets[hit.SetNumber - 1];

        if (hit.MacroIndex >= 0 && CurrentSet is not null && hit.MacroIndex < CurrentSet.Macros.Count)
            SelectedMacro = CurrentSet.Macros[hit.MacroIndex];
    }
}
