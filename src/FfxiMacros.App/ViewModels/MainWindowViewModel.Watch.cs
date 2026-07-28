using Avalonia.Threading;

using FfxiMacros.App.Localization;
using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;

namespace FfxiMacros.App.ViewModels;

/// <summary>
/// Following the game while it plays: the client writes a book's files when it switches away from
/// it, and the editor picks that up instead of showing what the folder held when it opened.
/// </summary>
/// <remarks>
/// Deliberately not a rescan. Rebuilding the library would throw away unsaved edits and reset the
/// selection every time the player changes job — so only what the file system knows is re-read:
/// timestamps, which sets exist, how full they are, and the book titles. The macros in memory are
/// left alone, apart from the one set on screen when it is untouched and the file under it moved.
/// </remarks>
public sealed partial class MainWindowViewModel
{
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _liveWatcher;
    private Timer? _settle;

    /// <summary>How long the writes have to stop before they are read back.</summary>
    /// <remarks>
    /// The client writes ten files in a row when it flushes a book; reacting to the first one would
    /// read the rest mid-write. A pause this long also keeps a job change to a single refresh.
    /// </remarks>
    private const int SettleMilliseconds = 900;

    /// <summary>Raised after the folder was re-read, so tests do not have to wait on a timer.</summary>
    public event Action? FilesChanged;

    private DispatcherTimer? _memoryPoll;

    /// <summary>
    /// Asks the running clients what book they have open, twice a second.
    /// </summary>
    /// <remarks>
    /// Polling, because a value in another process announces nothing when it changes. Three small
    /// reads per client is little enough to do at this rate, and the marker then follows a book
    /// picked from the game's own menu — the one case no file and no addon ever sees.
    /// </remarks>
    private void WatchOpenBooks()
    {
        _memoryPoll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                                            (_, _) => ReadOpenBooks());
        _memoryPoll.Start();
    }

    /// <summary>Re-reads the clients' memory and moves the marker when it has changed.</summary>
    public void ReadOpenBooks()
    {
        if (_library is null)
            return;

        LocateClients();
        ReportUnreadableClients();

        var books = ProbeOpenBooks();
        if (books.Count == _openBooks.Count && books.Zip(_openBooks).All(pair => pair.First == pair.Second))
            return;

        _openBooks = books;
        foreach (var character in Characters.OfType<CharacterNodeViewModel>())
            MarkBookOpenInGame(character);
    }

    private string _reportedUnreadable = "";

    /// <summary>
    /// Says so when Windows refuses to let the editor read a client.
    /// </summary>
    /// <remarks>
    /// A client started with more privileges than the editor cannot be read at all, and the only
    /// symptom is a marker that never appears — which says nothing about what to do. Told once,
    /// naming the client, with the one thing that fixes it.
    /// </remarks>
    private void ReportUnreadableClients()
    {
        string unreadable = string.Join(", ", _memory.Unreadable);
        if (unreadable == _reportedUnreadable)
            return;

        _reportedUnreadable = unreadable;
        if (unreadable.Length > 0)
            SetStatus(Loc.T("Status.ClientUnreadable", unreadable), error: true);
    }

    private bool _locating;

    /// <summary>
    /// Works out which character each client is playing, on a thread of its own.
    /// </summary>
    /// <remarks>
    /// Finding the title table means walking a couple of gigabytes, which takes seconds — far too
    /// long to do between two frames. It happens once per client: after that, reading the book is
    /// eight bytes, and the poll notices by itself if a client logs out and needs placing again.
    /// </remarks>
    private void LocateClients()
    {
        if (_locating || _library is null || !_memory.NeedsScan())
            return;

        var signatures = _library.Characters
            .Select(c => new CharacterSignature(c.Id, [.. c.Titles.All]))
            .ToList();

        _locating = true;
        Task.Run(() => _memory.Scan(signatures))
            .ContinueWith(_ => _locating = false, TaskScheduler.Default);
    }

    private void WatchFolder(string path)
    {
        StopWatching();

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                Filter = "mcr*.*",                       // the sets and the two title files
                IncludeSubdirectories = true,            // one folder per character
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;

            _log.Debug($"Watching {path} for changes made by the game.");
            WatchLiveReports();
            WatchOpenBooks();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be watched — a network share, a permission — is not a reason to
            // refuse to edit it. The editor simply stops noticing outside writes.
            _log.Warn($"Not watching {path}: {ex.Message}");
            _watcher = null;
        }
    }

    /// <summary>
    /// Watches the folder the Windower addon writes its reports into — the only source that knows
    /// the book a client has open while the player is still on it.
    /// </summary>
    private void WatchLiveReports()
    {
        if (_liveWatcher is not null)
            return;

        string folder = LiveStateFolder;
        LiveMacroStateStore.EnsureFolder(folder, _log);

        try
        {
            _liveWatcher = new FileSystemWatcher(folder, "*.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            _liveWatcher.Changed += OnFileEvent;
            _liveWatcher.Created += OnFileEvent;
            _liveWatcher.Deleted += OnFileEvent;
            _liveWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Not watching {folder}: {ex.Message}");
            _liveWatcher = null;
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _settle?.Dispose();
        _settle = null;
    }

    /// <summary>Restarts the quiet period; the folder is read back once the writes stop.</summary>
    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        _settle ??= new Timer(_ => Dispatcher.UIThread.Post(ReadFolderBack), null,
                              Timeout.Infinite, Timeout.Infinite);
        _settle.Change(SettleMilliseconds, Timeout.Infinite);
    }

    /// <summary>
    /// Re-reads what the disk says, without touching a single macro the user is editing.
    /// </summary>
    public void ReadFolderBack()
    {
        if (_library is null)
            return;

        _liveStates = LiveMacroStateStore.ReadAll(LiveStateFolder, _log);

        foreach (var character in Characters.OfType<CharacterNodeViewModel>())
        {
            ReloadTitles(character);

            foreach (var book in character.Books)
            {
                foreach (var set in book.Sets)
                    set.RefreshFromDisk();

                book.RefreshLabels();
            }

            // mcr.sys matches the watcher's filter, so a client saving its state lands here too.
            character.Character.RefreshCurrent();
            character.RefreshLabels();
            MarkBookOpenInGame(character);
        }

        OnPropertyChanged(nameof(CurrentBookTitle));
        OnPropertyChanged(nameof(CurrentSetTitle));
        ReportUnmatchedLiveState();
        ReloadCurrentSetIfUntouched();
        FilesChanged?.Invoke();
    }

    private string _lastUnmatchedReport = "";

    /// <summary>
    /// Says so when Windower reports a character the editor cannot place.
    /// </summary>
    /// <remarks>
    /// The addon knows the character by name; a USER folder is named after a number that has nothing
    /// to do with it — the id it does report belongs to some other numbering entirely. So the two
    /// are matched on the name the user gives a folder, and until they do, the report is useless.
    /// Saying which name is waiting to be matched turns that into a one-off, obvious fix.
    /// </remarks>
    private void ReportUnmatchedLiveState()
    {
        var unmatched = _liveStates.FirstOrDefault(state =>
            !Characters.OfType<CharacterNodeViewModel>().Any(c => LiveStateFor(c.Character) == state));

        // Only once per report: this runs on every write the game makes.
        string key = unmatched is null ? "" : $"{unmatched.CharacterName}#{unmatched.Book}";
        if (key == _lastUnmatchedReport)
            return;

        _lastUnmatchedReport = key;
        if (unmatched is not null)
            SetStatus(Loc.T("Status.LiveUnmatched", unmatched.CharacterName, unmatched.Book));
    }

    private void ReloadTitles(CharacterNodeViewModel character)
    {
        try
        {
            character.Character.Titles.Reload(_log);
        }
        catch (MacroFileException ex)
        {
            _log.Warn($"Could not re-read the titles of {character.Character.Id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Brings the set on screen back in step when the game rewrote it — but only when there is
    /// nothing of the user's to lose. Unsaved edits always win over what the client wrote.
    /// </summary>
    private void ReloadCurrentSetIfUntouched()
    {
        if (_currentSet is not { IsLoaded: true, IsDirty: false, ChangedOnDisk: true } set)
            return;

        try
        {
            set.Reload();
            SelectedMacro = set.Macros.FirstOrDefault(m => !m.IsEmpty) ?? set.Macros.FirstOrDefault();
            SetStatus(Loc.T("Status.RewrittenByGame", set.Info.FileName));
        }
        catch (MacroFileException ex)
        {
            SetStatus(ex.ToString(), error: true);
        }
    }
}
