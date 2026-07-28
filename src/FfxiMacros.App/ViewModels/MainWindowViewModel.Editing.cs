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

/// <summary>What the editor's own clipboard is holding.</summary>
public enum ClipboardKind
{
    None,
    Macro,
    Set,
    Book,
}

/// <summary>The three things that can be done to a whole book, all of them writing to disk.</summary>
public enum BookOperationKind
{
    Copy,
    Move,
    Clear,
}

/// <summary>What a pending book operation would do, waiting for the user to confirm it.</summary>
public sealed class PendingBookOperation(
    BookOperationKind kind, BookNodeViewModel source, BookNodeViewModel? target, int unsavedSets)
{
    public BookOperationKind Kind { get; } = kind;

    /// <summary>The book being copied, moved or emptied.</summary>
    public BookNodeViewModel Source { get; } = source;

    /// <summary>Where it goes — null when the book is simply being emptied.</summary>
    public BookNodeViewModel? Target { get; } = target;

    public bool Move => Kind == BookOperationKind.Move;

    /// <summary>
    /// Sets holding edits when the operation was asked for.
    /// </summary>
    /// <remarks>
    /// A book is copied on disk and the whole folder is re-read afterwards, which throws away every
    /// unsaved edit — anywhere, not only in the two books concerned. So the operation waits instead
    /// of refusing: the banner offers to save everything and carry on, which is what the user meant.
    /// </remarks>
    public int UnsavedSets { get; } = unsavedSets;

    public bool NeedsSave => UnsavedSets > 0;

    public string Question => Transfer + (NeedsSave ? Loc.T("Book.NeedsSave", UnsavedSets) : "");

    private string Transfer =>
        Kind == BookOperationKind.Clear || Target is not { } target
            ? Loc.T("Book.ClearQuestion",
                    Source.Info.Number, Source.Info.Title, Source.Parent.Character.Label, Source.Info.SetCount)
            : Loc.T(Move ? "Book.MoveQuestion" : "Book.CopyQuestion",
                    Source.Info.Number, Source.Info.Title, Source.Parent.Character.Label,
                    target.Info.Number, target.Info.Title, target.Parent.Character.Label)
              + Loc.T("Book.Overwrites", target.Info.SetCount)
              + Loc.T(Move ? "Book.SourceEmptied" : "Book.End");
}

public sealed partial class MainWindowViewModel
{
    private Macro? _macroClipboard;
    private MacroBook? _setClipboard;
    private (int Book, int Set) _setClipboardOrigin;
    private BookNodeViewModel? _bookClipboard;
    private ClipboardKind _clipboardKind;
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
    /// Who is in game, in three words, for the corner of the status bar.
    /// </summary>
    /// <remarks>
    /// This used to be a banner across the top of the window. It said something true and useful —
    /// once. Read for the twentieth time in a session it is noise, and it pushed the editor down the
    /// screen every time a client was running, so it is now a line in the status bar carrying the
    /// long version as its tooltip.
    /// </remarks>
    public string GameStatusSummary =>
        IsGameRunning ? Loc.T("Game.InGame", string.Join(", ", _runningClients)) : "";

    /// <summary>
    /// What being in game actually costs you, kept for the tooltip.
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
            SetStatus(IsGameRunning ? GameStatusSummary : Loc.T("Game.NobodyConnected"));
        });

    /// <summary>Re-reads which clients are running, and forgets any override once they are gone.</summary>
    public void RefreshGameState()
    {
        _runningClients = ProbeRunningClients();
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(GameStatusSummary));
        OnPropertyChanged(nameof(GameRunningWarning));
    }

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

    // ---------------------------------------------------------------- the editor's clipboard

    /// <summary>
    /// One clipboard per kind, rather than a single slot the three of them share.
    /// </summary>
    /// <remarks>
    /// Copying a book and then a macro would otherwise throw the book away, and a Ctrl+V landing on
    /// a set tab has to know whether there is a <em>set</em> to paste — not merely something. What
    /// is pasted is decided by what the pointer or the focus is on, so each kind keeps its own slot
    /// and they never overwrite each other.
    /// </remarks>
    public ClipboardKind Clipboard => _clipboardKind;

    public bool CanPasteMacro => _macroClipboard is not null;

    public bool CanPasteSet => _setClipboard is not null;

    public bool CanPasteBook => _bookClipboard is not null;

    /// <summary>What was copied last, shown in the status bar so the clipboard is never a guess.</summary>
    public string ClipboardSummary => _clipboardKind switch
    {
        ClipboardKind.Macro => Loc.T("Clipboard.Macro", NameOf(_macroClipboard!)),
        ClipboardKind.Set => Loc.T("Clipboard.Set", _setClipboardOrigin.Set, _setClipboardOrigin.Book),
        ClipboardKind.Book => Loc.T("Clipboard.Book", _bookClipboard!.Info.Number, _bookClipboard.Info.Title),
        _ => Loc.T("Clipboard.Empty"),
    };

    /// <summary>Ctrl+C and every « Copy » entry: what travels depends on what was clicked.</summary>
    public void CopyToClipboard(object? node)
    {
        switch (node)
        {
            case MacroSlotViewModel slot:
                CopyMacroToClipboard(slot);
                break;
            case SetNodeViewModel set:
                CopySetToClipboard(set);
                break;
            case BookNodeViewModel book:
                CopyBookToClipboard(book);
                break;
        }
    }

    /// <summary>Ctrl+V and every « Paste » entry: the target decides which clipboard is read.</summary>
    public void PasteFromClipboard(object? node)
    {
        switch (node)
        {
            case MacroSlotViewModel slot:
                PasteMacroFromClipboard(slot);
                break;
            case SetNodeViewModel set:
                PasteSetFromClipboard(set);
                break;
            case BookNodeViewModel book:
                PasteBookFromClipboard(book);
                break;
        }
    }

    // ---------------------------------------------------------------- one macro

    public void CopyMacroToClipboard(MacroSlotViewModel? slot)
    {
        if (slot is null)
            return;

        _macroClipboard = slot.Macro.Clone();
        ClipboardChanged(ClipboardKind.Macro);
        SetStatus(Loc.T("Status.Copied", slot.SlotLabel));
    }

    public void PasteMacroFromClipboard(MacroSlotViewModel? slot)
    {
        if (slot is null)
            return;

        if (_macroClipboard is null)
        {
            SetStatus(Loc.T("Status.NothingToPaste"), error: true);
            return;
        }

        MacroOperations.CopyMacro(_macroClipboard, slot.Macro);
        slot.NotifyMacroReplaced();
        slot.OnLineEdited();
        SelectedMacro = slot;
        SetStatus(Loc.T("Status.Pasted", slot.SlotLabel));
    }

    // ---------------------------------------------------------------- a whole set

    /// <summary>Takes a copy of the 20 macros of a set, loading it first if it was never opened.</summary>
    public void CopySetToClipboard(SetNodeViewModel? set)
    {
        if (set is null || !TryLoad(set))
            return;

        _setClipboard = set.Loaded!.Clone();
        _setClipboardOrigin = (set.Info.BookNumber, set.Info.SetNumber);
        ClipboardChanged(ClipboardKind.Set);
        SetStatus(Loc.T("Status.SetCopied", set.Info.SetNumber, set.Info.BookNumber));
    }

    /// <summary>
    /// Replaces the 20 macros of a set with the ones on the clipboard, in memory: nothing reaches
    /// the disk until the set is saved, so a paste onto the wrong set is undone by « Reload ».
    /// </summary>
    public void PasteSetFromClipboard(SetNodeViewModel? set)
    {
        if (set is null)
            return;

        if (_setClipboard is null)
        {
            SetStatus(Loc.T("Status.NothingToPaste"), error: true);
            return;
        }

        if (!TryLoad(set) || set.Loaded is not { } destination)
            return;

        // The macros travel; the file's version stamp does not. It belongs to the install that
        // wrote the target, and copying one in from another character would be a lie about the file.
        for (int index = 0; index < MacroBook.MacroCount; index++)
            destination.Macros[index] = _setClipboard.Macros[index].Clone();

        foreach (var slot in set.Macros)
            slot.NotifyMacroReplaced();

        set.MarkDirty();

        if (ReferenceEquals(set, _currentSet))
            SelectedMacro = set.Macros.FirstOrDefault(m => !m.IsEmpty) ?? set.Macros.FirstOrDefault();

        SetStatus(Loc.T("Status.SetPasted", set.Info.SetNumber, set.Info.BookNumber));
    }

    /// <summary>
    /// Empties the 20 macros of a set, in memory. « Reload » brings them back for as long as the
    /// set has not been saved — unlike emptying a book, which deletes files straight away.
    /// </summary>
    public void ClearSet(SetNodeViewModel? set)
    {
        if (set is null || !TryLoad(set) || set.Loaded is not { } loaded)
            return;

        if (loaded.IsEmpty)
        {
            SetStatus(Loc.T("Status.SetAlreadyEmpty", set.Info.SetNumber, set.Info.BookNumber));
            return;
        }

        foreach (var macro in loaded.Macros)
            macro.Clear();

        foreach (var slot in set.Macros)
            slot.NotifyMacroReplaced();

        set.MarkDirty();
        SetStatus(Loc.T("Status.SetCleared", set.Info.SetNumber, set.Info.BookNumber));
    }

    // ---------------------------------------------------------------- a whole book

    /// <summary>
    /// Puts a book on the clipboard. The node is remembered rather than its ten files read: a book
    /// is copied on disk, file by file, at the moment the paste is confirmed.
    /// </summary>
    public void CopyBookToClipboard(BookNodeViewModel? book)
    {
        if (book is null)
            return;

        _bookClipboard = book;
        ClipboardChanged(ClipboardKind.Book);
        SetStatus(Loc.T("Status.BookOnClipboard", book.Info.Number, book.Info.Title));
    }

    /// <summary>Proposes the copy; ten files are overwritten, so it still goes through the confirmation.</summary>
    public void PasteBookFromClipboard(BookNodeViewModel? target)
    {
        if (target is null)
            return;

        if (_bookClipboard is null)
        {
            SetStatus(Loc.T("Status.NothingToPaste"), error: true);
            return;
        }

        RequestBookTransfer(LiveBook(_bookClipboard), target, move: false);
    }

    /// <summary>
    /// The node standing for the same book in the tree as it is now. A book copy re-reads the whole
    /// folder, which builds new nodes — so the one on the clipboard would otherwise be a leftover of
    /// the tree as it was, and pasting the same book twice would work off stale file information.
    /// </summary>
    private BookNodeViewModel LiveBook(BookNodeViewModel book) =>
        Characters.OfType<CharacterNodeViewModel>()
            .FirstOrDefault(c => string.Equals(c.Character.Id, book.Parent.Character.Id, StringComparison.OrdinalIgnoreCase))
            ?.Books.FirstOrDefault(b => b.Info.Number == book.Info.Number)
        ?? book;

    /// <summary>Opens a set that was never read, reporting a file that refuses to load.</summary>
    private bool TryLoad(SetNodeViewModel set)
    {
        try
        {
            if (!set.IsLoaded)
                set.Load();

            return set.IsLoaded;
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.ToString(), error: true);
            return false;
        }
    }

    private void ClipboardChanged(ClipboardKind kind)
    {
        _clipboardKind = kind;
        OnPropertyChanged(nameof(Clipboard));
        OnPropertyChanged(nameof(CanPasteMacro));
        OnPropertyChanged(nameof(CanPasteSet));
        OnPropertyChanged(nameof(CanPasteBook));
        OnPropertyChanged(nameof(ClipboardSummary));
    }

    /// <summary>The name the game would show, for the clipboard summary.</summary>
    private static string NameOf(Macro macro) =>
        MacroRepair.VisibleInGame(macro.Name) is { Length: > 0 } name ? name : "—";

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
            ? Loc.T("Status.CopiedOnto", source.SlotLabel, target.SlotLabel)
            : Loc.T("Status.Swapped", source.SlotLabel, target.SlotLabel));
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

    /// <summary>
    /// Exports the whole book — every set that exists on disk, in one file.
    /// </summary>
    /// <remarks>
    /// A book is what a player thinks of as the macros for a job; exporting only the set on screen
    /// left the other nine behind. Sets the game has never written are skipped rather than written
    /// out empty, so a book with three sets in use produces a file with three sets in it.
    /// </remarks>
    private async Task ExportSetAsync()
    {
        if (_currentSet?.Loaded is null || SaveFileAsync is null)
            return;

        var bookNode = _currentSet.Parent;
        var book = bookNode.Info;
        string suggested = $"{bookNode.Parent.Character.Id}-book{book.Number}";

        string? path = await SaveFileAsync(suggested + MacroTextFormat.FileExtension, MacroTextFormat.FileExtension);
        if (path is null)
            return;

        try
        {
            var sets = new List<MacroSetExport>();
            foreach (var set in bookNode.Sets)
            {
                if (!set.Info.Exists)
                    continue;

                if (!set.IsLoaded)
                    set.Load();

                if (set.Loaded is not null)
                    sets.Add(new MacroSetExport(set.Info.SetNumber, set.Loaded));
            }

            if (sets.Count == 0)
                sets.Add(new MacroSetExport(_currentSet.Info.SetNumber, _currentSet.Loaded));

            bool json = path.EndsWith(MacroJsonFormat.FileExtension, StringComparison.OrdinalIgnoreCase);
            string content = json
                ? MacroJsonFormat.Export(sets, bookNode.Parent.Character.Id, book.Number, book.Title)
                : MacroTextFormat.Export(sets,
                    $"{bookNode.Parent.Character.Label} · book {book.Number} ({book.Title})");

            LongPath.WriteAllBytesAtomic(path, System.Text.Encoding.UTF8.GetBytes(content));
            SetStatus(Loc.T(sets.Count == 1 ? "Status.ExportedOne" : "Status.Exported", path, sets.Count));
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

            var sets = json ? MacroJsonFormat.ImportSets(content) : MacroTextFormat.ImportSets(content);
            int applied = ApplyImportedSets(sets);

            SetStatus(Loc.T(applied == 1 ? "Status.ImportedOne" : "Status.Imported", path, applied));
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.ToString(), error: true);
        }
    }

    /// <summary>
    /// Puts imported sets where they belong: a file that numbers its sets goes into those sets of
    /// the current book, and one that does not — a single-set export — goes into the set on screen.
    /// </summary>
    /// <returns>How many sets were written into.</returns>
    private int ApplyImportedSets(IReadOnlyList<MacroSetExport> sets)
    {
        int applied = 0;

        foreach (var imported in sets)
        {
            var target = imported.SetNumber == 0
                ? _currentSet
                : _currentSet!.Parent.Sets.FirstOrDefault(s => s.Info.SetNumber == imported.SetNumber);

            if (target is null)
                continue;

            if (!target.IsLoaded)
                target.Load();
            if (target.Loaded is not { } destination)
                continue;

            for (int index = 0; index < MacroBook.MacroCount; index++)
                destination.Macros[index] = imported.Book.Macros[index].Clone();

            foreach (var slot in target.Macros)
                slot.NotifyMacroReplaced();

            target.MarkDirty();
            applied++;
        }

        RaiseDirtyState();
        RaiseCommandStates();
        return applied;
    }

    // ---------------------------------------------------------------- renaming, in the tree

    /// <summary>
    /// Puts a row into edit mode — a book's title, or the name a character folder goes by.
    /// </summary>
    /// <remarks>
    /// A book's title is the one the game shows on the macro bar, written to <c>mcr.ttl</c>. A
    /// character's name is the editor's own: the folder is a hexadecimal number that says nothing,
    /// and naming it is also what lets a Windower report find the character it belongs to.
    /// </remarks>
    public void BeginRename(TreeNodeViewModel? node)
    {
        if (node is not { CanRename: true })
            return;

        foreach (var other in Characters.OfType<TreeNodeViewModel>().Concat(
                     Characters.OfType<CharacterNodeViewModel>().SelectMany(c => c.Books)))
        {
            other.IsRenaming = false;
        }

        node.RenameDraft = node switch
        {
            BookNodeViewModel book => book.Info.Title,
            CharacterNodeViewModel character => character.Character.DisplayName ?? "",
            _ => "",
        };

        node.IsRenaming = true;
    }

    public void CancelRename(TreeNodeViewModel? node)
    {
        if (node is not null)
            node.IsRenaming = false;
    }

    public void CommitRename(TreeNodeViewModel? node)
    {
        switch (node)
        {
            case BookNodeViewModel book:
                CommitRenameBook(book);
                break;
            case CharacterNodeViewModel character:
                CommitRenameCharacter(character);
                break;
        }
    }

    /// <summary>Writes the new title to the character's title file, or says why it does not fit.</summary>
    private void CommitRenameBook(BookNodeViewModel book)
    {
        if (!book.IsRenaming)
            return;

        string title = book.RenameDraft.Trim();
        if (title.Length == 0 || string.Equals(title, book.Info.Title, StringComparison.Ordinal))
        {
            book.IsRenaming = false;
            return;
        }

        try
        {
            if (!MayWriteToDisk())
                return;

            BackupOnce(book.Parent.Character);
            MacroOperations.RenameBook(book.Info, title, _log);
            book.IsRenaming = false;
            book.RefreshUpwards();

            SetStatus(Loc.T("Status.BookRenamed", book.Info.Number, title)
                      + (IsGameRunning ? Loc.T("Game.SaveAdvice", book.Info.Number) : ""));
        }
        catch (MacroFileException ex)
        {
            // Left in edit mode on purpose: the text is still there to shorten.
            SetStatus(ex.Message, error: true);
        }
    }

    /// <summary>
    /// Names a character folder. Nothing is written to the game's files — this lives in the editor's
    /// settings, and it is how a report from Windower is matched to a folder.
    /// </summary>
    private void CommitRenameCharacter(CharacterNodeViewModel character)
    {
        if (!character.IsRenaming)
            return;

        string name = character.RenameDraft.Trim();
        character.IsRenaming = false;

        if (string.Equals(name, character.Character.DisplayName ?? "", StringComparison.Ordinal))
            return;

        character.Rename(name);
        _settings.SetName(character.Character.Id, name);
        TrySaveSettings();

        // The name is the link to the addon's report, so the marker can appear the moment it is set.
        foreach (var node in Characters.OfType<CharacterNodeViewModel>())
            MarkBookOpenInGame(node);

        SetStatus(name.Length == 0
            ? Loc.T("Status.CharacterUnnamed", character.Character.Id)
            : Loc.T("Status.CharacterRenamed", character.Character.Id, name));
    }

    // ---------------------------------------------------------------- book copy / move

    public PendingBookOperation? PendingBookOperation
    {
        get => _pendingBookOperation;
        private set
        {
            if (!SetField(ref _pendingBookOperation, value))
                return;

            OnPropertyChanged(nameof(HasPendingBookOperation));
            _confirmBookOperationCommand?.RaiseCanExecuteChanged();
            _saveAllAndConfirmCommand?.RaiseCanExecuteChanged();
        }
    }

    public bool HasPendingBookOperation => _pendingBookOperation is not null;

    private RelayCommand? _confirmBookOperationCommand;
    public RelayCommand ConfirmBookOperationCommand =>
        _confirmBookOperationCommand ??= new RelayCommand(ConfirmBookOperation, () => _pendingBookOperation is { NeedsSave: false });

    private RelayCommand? _saveAllAndConfirmCommand;

    /// <summary>Saves every pending edit, then carries out the book operation that was waiting on them.</summary>
    public RelayCommand SaveAllAndConfirmCommand =>
        _saveAllAndConfirmCommand ??= new RelayCommand(SaveAllThenConfirm, () => _pendingBookOperation is { NeedsSave: true });

    private RelayCommand? _cancelBookOperationCommand;
    public RelayCommand CancelBookOperationCommand =>
        _cancelBookOperationCommand ??= new RelayCommand(() => { PendingBookOperation = null; SetStatus(Loc.T("Status.Cancelled")); });

    /// <summary>
    /// A dropped or pasted book is never applied straight away: copying a book overwrites ten files,
    /// so the user is shown exactly what will happen and has to confirm.
    /// </summary>
    public void RequestBookTransfer(BookNodeViewModel source, BookNodeViewModel target, bool move)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (ReferenceEquals(source, target))
            return;

        // Unsaved edits used to turn this into a flat refusal, reported in the status bar — which
        // reads as « nothing happened » to anyone whose eyes are on the book they just clicked.
        // The operation is proposed either way; the banner then asks for the save it needs.
        Propose(new PendingBookOperation(
            move ? BookOperationKind.Move : BookOperationKind.Copy, source, target, DirtyCount));
    }

    /// <summary>
    /// Asks to empty a book: its set files are deleted and its title reset. Nothing an « undo »
    /// could bring back, so it goes through the same confirmation as a copy.
    /// </summary>
    public void RequestBookClear(BookNodeViewModel? book)
    {
        if (book is null)
            return;

        if (book.IsEmptyAndUntitled)
        {
            SetStatus(Loc.T("Status.NothingToClear", book.Info.Number));
            return;
        }

        Propose(new PendingBookOperation(BookOperationKind.Clear, book, target: null, DirtyCount));
    }

    private void Propose(PendingBookOperation operation)
    {
        PendingBookOperation = operation;
        SetStatus(operation.Question, error: operation.NeedsSave);
    }

    private void SaveAllThenConfirm()
    {
        if (_pendingBookOperation is not { } operation)
            return;

        SaveAll();
        if (DirtySets.Any())
            return;   // something refused to save; SaveAll has said which, and the operation waits on

        PendingBookOperation = new PendingBookOperation(operation.Kind, operation.Source, operation.Target, 0);
        ConfirmBookOperation();
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
                // Emptying a book deletes files: the backup is the only way back, so it comes first.
                BackupOnce((operation.Target ?? operation.Source).Parent.Character);
                if (operation.Move || operation.Kind == BookOperationKind.Clear)
                    BackupOnce(operation.Source.Parent.Character);
            }

            if (operation.Target is not { } target)
            {
                MacroOperations.ClearBook(operation.Source.Info, _log);
                SetStatus(Loc.T("Status.BookCleared", operation.Source.Info.Number));
            }
            else
            {
                if (operation.Move)
                {
                    MacroOperations.MoveBook(operation.Source.Info, target.Info, _log);
                    }
                else
                    MacroOperations.CopyBook(operation.Source.Info, target.Info, keepTargetTitle: false, log: _log);

                SetStatus(Loc.T(operation.Move ? "Status.BookMoved" : "Status.BookCopied",
                                operation.Source.Info.Number, target.Info.Number));
            }
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
