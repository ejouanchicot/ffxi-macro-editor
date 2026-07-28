using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;

namespace FfxiMacros.Core.Discovery;

/// <summary>
/// The 40 book titles of a character, spread across <c>mcr.ttl</c> (books 1-20) and
/// <c>mcr_2.ttl</c> (books 21-40). Presents them as one list and keeps both files in step.
/// </summary>
public sealed class CharacterTitles
{
    public const int BookCount = MacroFileNaming.BooksPerCharacter;   // 40

    private BookTitleSet _primary;
    private BookTitleSet _secondary;

    private CharacterTitles(string folder, BookTitleSet primary, BookTitleSet secondary)
    {
        Folder = folder;
        _primary = primary;
        _secondary = secondary;
        _primary.IsSecondary = false;
        _secondary.IsSecondary = true;
    }

    public string Folder { get; }

    public string PrimaryPath => Path.Combine(Folder, BookTitleSet.PrimaryFileName);

    public string SecondaryPath => Path.Combine(Folder, BookTitleSet.SecondaryFileName);

    /// <summary>False when a title file was missing on disk; a save will create it.</summary>
    public bool PrimaryExisted { get; private set; }

    public bool SecondaryExisted { get; private set; }

    /// <summary>True when either file's stored MD5 did not match its data.</summary>
    public bool HasDigestMismatch => !_primary.DigestWasValid || !_secondary.DigestWasValid;

    /// <summary>Title of a 1-based book number, 1..40. Empty string means "untitled".</summary>
    public string this[int bookNumber]
    {
        get
        {
            var (set, index) = Locate(bookNumber);
            return set.Titles[index];
        }
        set
        {
            var (set, index) = Locate(bookNumber);
            set.Titles[index] = value ?? "";
        }
    }

    /// <summary>All 40 titles in book order, with the game's <c>BookNN</c> placeholder for empty ones.</summary>
    public IEnumerable<string> All =>
        Enumerable.Range(1, BookCount)
            .Select(n => string.IsNullOrEmpty(this[n]) ? BookTitleSet.DefaultTitle(n) : this[n]);

    /// <summary>
    /// Reads both title files. A missing or unreadable file yields default titles instead of an
    /// error: a character can perfectly well have macros and no title file yet.
    /// </summary>
    public static CharacterTitles Load(string folder, IMacroLog? log = null)
    {
        var (primary, primaryExisted) = LoadOne(Path.Combine(folder, BookTitleSet.PrimaryFileName), log);
        var (secondary, secondaryExisted) = LoadOne(Path.Combine(folder, BookTitleSet.SecondaryFileName), log);

        return new CharacterTitles(folder, primary, secondary)
        {
            PrimaryExisted = primaryExisted,
            SecondaryExisted = secondaryExisted,
        };
    }

    /// <summary>
    /// Re-reads both files, for when something else wrote them — the game does, on its own schedule.
    /// </summary>
    /// <remarks>
    /// Safe to call at any time: a title is written the moment it is changed, so there is never an
    /// edit in memory here waiting to be saved that this could throw away.
    /// </remarks>
    public void Reload(IMacroLog? log = null)
    {
        var (primary, primaryExisted) = LoadOne(PrimaryPath, log);
        var (secondary, secondaryExisted) = LoadOne(SecondaryPath, log);

        primary.IsSecondary = false;
        secondary.IsSecondary = true;

        _primary = primary;
        _secondary = secondary;
        PrimaryExisted = primaryExisted;
        SecondaryExisted = secondaryExisted;
    }

    /// <summary>Writes both title files, recomputing their MD5.</summary>
    public void Save(bool truncate = false)
    {
        _primary.Save(PrimaryPath, truncate);
        _secondary.Save(SecondaryPath, truncate);
    }

    /// <summary>Writes only the file holding <paramref name="bookNumber"/>.</summary>
    public void SaveHalfFor(int bookNumber, bool truncate = false)
    {
        var (set, _) = Locate(bookNumber);
        set.Save(set.IsSecondary ? SecondaryPath : PrimaryPath, truncate);
    }

    private static (BookTitleSet Set, bool Existed) LoadOne(string path, IMacroLog? log)
    {
        if (!File.Exists(path))
        {
            log.Debug($"No title file at {path}; using default book names.");
            return (NewEmptySet(), false);
        }

        try
        {
            var set = BookTitleSet.Load(path);
            if (!set.DigestWasValid)
                log.Warn($"{path}: stored MD5 does not match the data; it will be rewritten on save.");
            return (set, true);
        }
        catch (MacroFileException ex)
        {
            log.Warn($"Ignoring unreadable title file {path}: {ex.Message}");
            return (NewEmptySet(), false);
        }
    }

    /// <summary>A fresh title file carries version 1, the value FFXI writes on a clean install.</summary>
    private static BookTitleSet NewEmptySet() => new() { Version = 1 };

    private (BookTitleSet Set, int Index) Locate(int bookNumber)
    {
        if (bookNumber is < 1 or > BookCount)
            throw new ArgumentOutOfRangeException(nameof(bookNumber), bookNumber, $"Book must be 1..{BookCount}.");

        return bookNumber <= BookTitleSet.TitleCount
            ? (_primary, bookNumber - 1)
            : (_secondary, bookNumber - BookTitleSet.TitleCount - 1);
    }
}
