using System.Collections.ObjectModel;
using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Settings;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IMacroLog? _log;
    private readonly HashSet<string> _backedUpCharacters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the Windower addon last reported, per character.</summary>
    private IReadOnlyList<LiveMacroState> _liveStates = [];

    /// <summary>Where the addon writes its reports. Settable so tests stay out of the real folder.</summary>
    public string LiveStateFolder { get; set; } = LiveMacroStateStore.DefaultFolder;

    /// <summary>What each running client has open, read from its memory.</summary>
    private IReadOnlyList<OpenBook> _openBooks = [];

    private readonly GameMemoryProbe _memory;

    /// <summary>
    /// Reads the book each client has open. Replaceable, so tests never touch a live process.
    /// </summary>
    public Func<IReadOnlyList<OpenBook>> ProbeOpenBooks { get; set; } = () => [];

    private EditorSettings _settings;
    private MacroLibrary? _library;
    private TreeNodeViewModel? _selectedNode;
    private BookNodeViewModel? _currentBook;
    private SetNodeViewModel? _currentSet;
    private MacroSlotViewModel? _selectedMacro;
    private string _status = "";
    private bool _statusIsError;

    public MainWindowViewModel(EditorSettings settings, IMacroLog? log = null)
    {
        _settings = settings;
        _log = log;

        _memory = new GameMemoryProbe(log);
        ProbeOpenBooks = _memory.Read;

        ChooseFolderCommand = new AsyncRelayCommand(ChooseFolderAsync);
        RefreshCommand = new RelayCommand(Refresh, () => _library is not null);
        SaveCommand = new RelayCommand(SaveCurrent, CanSaveCurrent);
        SaveAllCommand = new RelayCommand(SaveAll, () => DirtySets.Any());
        ReloadCommand = new RelayCommand(ReloadCurrent, () => _currentSet?.IsLoaded == true);
        PreviousSetCommand = new RelayCommand(() => StepSet(-1), () => _currentBook is not null);
        NextSetCommand = new RelayCommand(() => StepSet(+1), () => _currentBook is not null);
        SelectSetCommand = new RelayCommand<SetNodeViewModel>(set => CurrentSet = set);
        SelectMacroCommand = new RelayCommand<MacroSlotViewModel>(macro => SelectedMacro = macro);
    }

    /// <summary>Set by the view: opens the folder picker and returns the chosen path.</summary>
    public Func<string?, Task<string?>>? PickFolderAsync { get; set; }

    public ObservableCollection<TreeNodeViewModel> Characters { get; } = [];

    public AsyncRelayCommand ChooseFolderCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand SaveAllCommand { get; }

    public RelayCommand ReloadCommand { get; }

    public RelayCommand PreviousSetCommand { get; }

    public RelayCommand NextSetCommand { get; }

    public RelayCommand<SetNodeViewModel> SelectSetCommand { get; }

    public RelayCommand<MacroSlotViewModel> SelectMacroCommand { get; }

    public string UserFolder => _settings.UserFolder ?? Loc.T("Books.NoFolder");

    public bool HasLibrary => _library is not null;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetField(ref _statusIsError, value);
    }

    /// <summary>Sets with unsaved edits, across every character.</summary>
    public IEnumerable<SetNodeViewModel> DirtySets =>
        Characters.OfType<CharacterNodeViewModel>()
            .SelectMany(c => c.Books)
            .SelectMany(b => b.Sets)
            .Where(s => s.IsDirty);

    /// <summary>Where auto-translate names come from, or why they are unavailable.</summary>
    public string AutoTranslateStatus =>
        Core.Text.FfxiText.DefaultAutoTranslate.IsEmpty
            ? "Auto-traduction : noms indisponibles (Windower introuvable)"
            : $"Auto-traduction : {Core.Text.FfxiText.DefaultAutoTranslate.Count} phrases";

    public int DirtyCount => DirtySets.Count();

    public string DirtySummary => DirtyCount switch
    {
        0 => Loc.T("Status.NoChanges"),
        1 => Loc.T("Status.DirtyOne"),
        _ => Loc.T("Status.DirtyMany", DirtyCount),
    };

    public TreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetField(ref _selectedNode, value))
                return;

            if (value is BookNodeViewModel book)
                OpenBook(book);
        }
    }

    /// <summary>The book whose 10 sets are offered as tabs.</summary>
    public BookNodeViewModel? CurrentBook
    {
        get => _currentBook;
        private set
        {
            if (SetField(ref _currentBook, value))
            {
                OnPropertyChanged(nameof(CurrentSets));
                OnPropertyChanged(nameof(CurrentSetWheel));
                OnPropertyChanged(nameof(HasCurrentBook));
                OnPropertyChanged(nameof(CurrentBookTitle));
            }
        }
    }

    public bool HasCurrentBook => _currentBook is not null;

    public string CurrentBookTitle => _currentBook is null
        ? ""
        : $"{_currentBook.Parent.Character.Label}   ·   Book {_currentBook.Info.Number} « {_currentBook.Info.Title} »";

    /// <summary>The 10 sets of the selected book, in order.</summary>
    public IReadOnlyList<SetNodeViewModel> CurrentSets => _currentBook?.Sets ?? [];

    /// <summary>
    /// The order the game's up and down arrows walk the sets, anchored on set 1: going up from it
    /// reaches 10, then 9, 8, 7; going down reaches 2, 3, 4… Laying them out this way makes the
    /// wrap-around visible, which is what players rely on when they park quick macros on sets 10
    /// and 2 either side of their home set.
    /// </summary>
    private static readonly int[] WheelOrder = [6, 7, 8, 9, 0, 1, 2, 3, 4, 5];

    public IReadOnlyList<SetNodeViewModel> CurrentSetWheel =>
        _currentBook is null ? [] : Array.ConvertAll(WheelOrder, i => _currentBook.Sets[i]);

    /// <summary>
    /// The set being edited. Settable, because the set tabs bind straight to it — selecting a tab
    /// opens the file.
    /// </summary>
    public SetNodeViewModel? CurrentSet
    {
        get => _currentSet;
        set
        {
            var previous = _currentSet;
            if (!SetField(ref _currentSet, value))
                return;

            if (previous is not null)
                previous.IsCurrent = false;
            if (value is not null)
                value.IsCurrent = true;

            OnPropertyChanged(nameof(CurrentSetTitle));
            OnPropertyChanged(nameof(CurrentSetPath));
            OnPropertyChanged(nameof(HasCurrentSet));
            RaiseCommandStates();

            if (value is not null)
                OpenSet(value);
            else
                SelectedMacro = null;
        }
    }

    public bool HasCurrentSet => _currentSet is not null;

    public string CurrentSetTitle => _currentSet is null
        ? ""
        : Loc.T("Set.Title", _currentSet.Parent.Parent.Character.Label, _currentSet.Info.BookNumber,
                _currentSet.Parent.Info.Title, _currentSet.Info.SetNumber);

    public string CurrentSetPath => _currentSet?.Info.FullPath ?? "";

    public MacroSlotViewModel? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            var previous = _selectedMacro;
            if (!SetField(ref _selectedMacro, value))
                return;

            if (previous is not null)
                previous.IsSelected = false;
            if (value is not null)
                value.IsSelected = true;

            OnPropertyChanged(nameof(HasSelectedMacro));
        }
    }

    public bool HasSelectedMacro => _selectedMacro is not null;

    // ---------------------------------------------------------------- startup

    /// <summary>Opens the remembered folder, or auto-detects one on first run.</summary>
    public void Initialize()
    {
        if (_settings.UserFolder is not null && Directory.Exists(_settings.UserFolder))
        {
            OpenFolder(_settings.UserFolder, remember: false);
            return;
        }

        var best = UserFolderLocator.DetectBest(_settings.UserFolder, _log);
        if (best is null)
        {
            SetStatus(Loc.T("Status.NoInstall"), error: true);
            return;
        }

        OpenFolder(best.Path, remember: true);
        SetStatus(Loc.T("Status.Detected", best.Source, best.Path));
    }

    private async Task ChooseFolderAsync()
    {
        if (PickFolderAsync is null)
            return;

        string? picked = await PickFolderAsync(_settings.UserFolder);
        if (picked is null)
            return;

        string? resolved = UserFolderLocator.Resolve(picked);
        if (resolved is null)
        {
            SetStatus(Loc.T("Status.NoCharacterData", picked), error: true);
            return;
        }

        OpenFolder(resolved, remember: true);
    }

    private void OpenFolder(string path, bool remember)
    {
        try
        {
            _library = MacroLibrary.Scan(path, _settings, _log);
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.Message, error: true);
            return;
        }

        if (remember)
        {
            _settings.UseUserFolder(_library.UserFolder);
            TrySaveSettings();
        }

        BuildTree();
        RefreshGameState();
        WatchFolder(_library.UserFolder);

        // Opening a folder reports what was found, whether or not a client is running: who is in
        // game is shown in the corner of the status bar, and does not need to shout over this.
        SetStatus($"{_library.Characters.Count} personnage(s), {_library.Characters.Sum(c => c.SetFileCount)} set(s) de macros.");

        // Unless Windower is reporting someone this folder cannot place — that needs saying, and
        // saying at startup rather than only when a file happens to change afterwards.
        ReportUnmatchedLiveState();
    }

    private void Refresh()
    {
        if (_library is null)
            return;

        if (DirtyCount > 0)
        {
            SetStatus($"{DirtySummary} : enregistre ou recharge avant d'actualiser.", error: true);
            return;
        }

        OpenFolder(_library.UserFolder, remember: false);
    }

    private void BuildTree()
    {
        _liveStates = LiveMacroStateStore.ReadAll(LiveStateFolder, _log);
        Characters.Clear();
        CurrentSet = null;
        SelectedMacro = null;

        foreach (var character in _library!.Characters)
        {
            var node = new CharacterNodeViewModel(character);
            foreach (var set in node.Books.SelectMany(b => b.Sets))
                set.Changed = NotifyEdited;

            MarkBookOpenInGame(node);
            Characters.Add(node);
        }

        // Open straight onto the most recently played character's first real book, so the window
        // shows macros rather than an empty pane. The list itself is in name order, so this has to
        // ask which character was played last rather than take the one at the top.
        var recent = Characters.OfType<CharacterNodeViewModel>()
            .MaxBy(c => c.Character.LastWriteUtc);

        if (recent is not null)
        {
            recent.IsExpanded = true;
            var book = recent.Books.FirstOrDefault(b => b.Info.Exists) ?? recent.Books.FirstOrDefault();
            if (book is not null)
                SelectedNode = book;
        }

        OnPropertyChanged(nameof(UserFolder));
        OnPropertyChanged(nameof(HasLibrary));
        RaiseDirtyState();
        RaiseCommandStates();
    }

    // ---------------------------------------------------------------- editing

    /// <summary>Opens a book and lands on its first set holding macros.</summary>
    private void OpenBook(BookNodeViewModel book)
    {
        CurrentBook = book;
        CurrentSet = book.Sets.FirstOrDefault(s => s.Info.Exists) ?? book.Sets[0];
    }

    /// <summary>Moves to the previous or next set of the current book, the way the game pages through them.</summary>
    private void StepSet(int delta)
    {
        if (_currentBook is null || _currentSet is null)
            return;

        int index = Array.IndexOf(_currentBook.Sets, _currentSet) + delta;
        if (index >= 0 && index < _currentBook.Sets.Length)
            CurrentSet = _currentBook.Sets[index];
    }

    private void OpenSet(SetNodeViewModel set)
    {
        try
        {
            if (!set.IsLoaded)
                set.Load();

            SelectedMacro = set.Macros.FirstOrDefault(m => !m.IsEmpty) ?? set.Macros.FirstOrDefault();

            if (set.Loaded?.DigestWasValid == false)
                SetStatus(Loc.T("Status.Md5Mismatch", set.Info.FileName), error: true);
            else
                SetStatus(Loc.T("Status.Opened", set.Info.FileName));
        }
        catch (MacroFileException ex)
        {
            SelectedMacro = null;
            SetStatus(ex.ToString(), error: true);
        }
        finally
        {
            // Repair, Export and Import need the set on hand, so they can only be judged once it
            // has been read. The selection setter asks too early — at that point nothing is loaded
            // yet. A set that failed to load leaves them refusing to run, which is right.
            RaiseCommandStates();
        }
    }

    private bool CanSaveCurrent() => _currentSet is { IsDirty: true, HasError: false };

    private void SaveCurrent()
    {
        if (_currentSet is null)
            return;

        if (SaveSet(_currentSet))
            SetStatus(Loc.T("Status.Saved", _currentSet.Info.FileName, SaveAdvice(_currentSet)));
    }

    private void SaveAll()
    {
        var dirty = DirtySets.ToList();
        var blocked = dirty.Where(s => s.HasError).ToList();
        int saved = dirty.Except(blocked).Count(SaveSet);

        if (blocked.Count > 0)
            SetStatus(Loc.T("Status.SavedBlocked", saved, blocked.Count), error: true);
        else
            SetStatus(Loc.T("Status.SavedAll", saved));
    }

    /// <summary>Backs the character up once per session, then writes the set.</summary>
    private bool SaveSet(SetNodeViewModel set)
    {
        if (!MayWriteToDisk())
            return false;

        var character = set.Parent.Parent.Character;

        try
        {
            BackupOnce(character);
            set.Save();
            RaiseDirtyState();
            RaiseCommandStates();
            return true;
        }
        catch (MacroFileException ex)
        {
            _backedUpCharacters.Remove(character.Id);
            SetStatus(ex.ToString(), error: true);
            return false;
        }
    }

    /// <summary>Copies a character's macro files aside, once per session, before the first write.</summary>
    private void BackupOnce(CharacterFolder character)
    {
        if (!_settings.BackupBeforeSave || !_backedUpCharacters.Add(character.Id))
            return;

        string target = MacroLibrary.BackupCharacter(
            character, _settings.BackupFolder ?? SettingsStore.DefaultBackupFolder, log: _log);
        _log.Info($"Backup before first write: {target}");
    }

    private void ReloadCurrent()
    {
        if (_currentSet is null)
            return;

        try
        {
            _currentSet.Reload();
            SelectedMacro = _currentSet.Macros.FirstOrDefault(m => !m.IsEmpty) ?? _currentSet.Macros.FirstOrDefault();
            RaiseDirtyState();
            RaiseCommandStates();
            SetStatus(Loc.T("Status.Reloaded", _currentSet.Info.FileName));
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.ToString(), error: true);
        }
    }

    /// <summary>
    /// Flags the book the game says the character is on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Taken from <c>mcr.sys</c>, which the client writes for itself — no guessing involved. The
    /// first attempt did guess, marking the book whose files were written most recently, and it was
    /// wrong every time in the most confusing way possible: the client writes the book it is
    /// <em>leaving</em>, so the marker always pointed at the book the player had just come from.
    /// </para>
    /// <para>
    /// That file is written when the client saves its state — logging in, logging out — and not at
    /// every book change, so it goes stale the moment the player starts moving around. Any macro
    /// file written after it is proof of exactly that: the client has been switching books since,
    /// and where it went is not written down anywhere. The marker is then dropped, because no
    /// marker beats one pointing at the book the player left an hour ago.
    /// </para>
    /// </remarks>
    private void MarkBookOpenInGame(CharacterNodeViewModel character)
    {
        var folder = character.Character;
        BookNodeViewModel? open = null;

        // The client's own memory holds the answer, so nothing else is asked when it can be read.
        if (OpenBookFor(folder) is { } fromMemory)
        {
            open = character.Books.FirstOrDefault(b => b.Info.Number == fromMemory.Book);
            foreach (var book in character.Books)
                book.SetOpenInGame(ReferenceEquals(book, open), OpenBookSource.Memory);

            return;
        }

        // A Windower report beats anything worked out from the files: it hears the /macro book
        // commands go past, which covers everything except a book picked from the game's own menu.
        if (LiveStateFor(folder) is { } live)
        {
            var reported = character.Books.FirstOrDefault(b => b.Info.Number == live.Book);

            // Unless the client has written that book since — which is what leaving it looks like.
            // The addon only hears the `/macro book` commands; a book changed from the game's own
            // menu passes it by, and this is what keeps the marker from pointing at a book the
            // player walked away from ten minutes ago.
            if (reported is not null && reported.Info.LastWriteUtc <= live.WrittenUtc + SameFlush)
                open = reported;

            foreach (var book in character.Books)
                book.SetOpenInGame(ReferenceEquals(book, open), OpenBookSource.Windower);

            return;
        }

        if (folder.CurrentBookNumber is { } number && folder.CurrentWrittenUtc is { } recordedAt)
        {
            var newestWrite = character.Books
                .Where(b => b.Info.Exists)
                .Select(b => b.Info.LastWriteUtc)
                .DefaultIfEmpty()
                .Max();

            // The client flushes its books and its state together when it leaves, so writes landing
            // alongside mcr.sys are part of that same save rather than a sign of a later move.
            if (newestWrite <= recordedAt + SameFlush)
                open = character.Books.FirstOrDefault(b => b.Info.Number == number);
        }

        foreach (var book in character.Books)
            book.SetOpenInGame(ReferenceEquals(book, open), OpenBookSource.File);
    }

    /// <summary>
    /// What the client's memory says for a character.
    /// </summary>
    /// <remarks>
    /// Matched on the folder itself, not on a name: a client is recognised by the book titles it is
    /// holding, which are that character's and no one else's. Nothing to label, nothing to declare.
    /// </remarks>
    private OpenBook? OpenBookFor(CharacterFolder character) =>
        _openBooks.FirstOrDefault(open =>
            string.Equals(open.CharacterId, character.Id, StringComparison.OrdinalIgnoreCase));

    private static readonly TimeSpan SameFlush = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The addon's report for a character, matched on the folder name.
    /// </summary>
    /// <remarks>
    /// A character's USER folder is named after its id in hexadecimal, and Windower knows that same
    /// id — so the two line up without the user having to tell the editor anything. The name is
    /// tried as well, for a folder that was given one by hand.
    /// </remarks>
    private LiveMacroState? LiveStateFor(CharacterFolder character) =>
        _liveStates.FirstOrDefault(state =>
            string.Equals(state.CharacterId, character.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.CharacterName, character.DisplayName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Called by the view whenever an edit happened, to refresh the toolbar state.</summary>
    public void NotifyEdited()
    {
        RaiseDirtyState();
        RaiseCommandStates();
    }

    public void SetStatus(string message, bool error = false)
    {
        Status = message;
        StatusIsError = error;
        if (error)
            _log.Warn(message);
        else
            _log.Info(message);
    }

    private void RaiseDirtyState()
    {
        OnPropertyChanged(nameof(DirtyCount));
        OnPropertyChanged(nameof(DirtySummary));
    }

    /// <summary>
    /// Re-asks every command whether it can run. Missing one here leaves its button greyed out for
    /// the rest of the session, since nothing else ever re-evaluates it.
    /// </summary>
    private void RaiseCommandStates()
    {
        SaveCommand.RaiseCanExecuteChanged();
        SaveAllCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        RepairCommand.RaiseCanExecuteChanged();
        ExportSetCommand.RaiseCanExecuteChanged();
        ImportSetCommand.RaiseCanExecuteChanged();
    }

    private void TrySaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (MacroFileException ex)
        {
            SetStatus(Loc.T("Status.SettingsNotSaved", ex.Message), error: true);
        }
    }

    /// <summary>Replaces the settings object, used after an external change.</summary>
    public void UseSettings(EditorSettings settings)
    {
        _settings = settings;
        OnPropertyChanged(nameof(UserFolder));
    }
}
