using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;

namespace FfxiMacros.Core.Discovery;

/// <summary>
/// One character: a hexadecimal folder inside <c>USER</c> holding up to 400 macro set files
/// plus the two title files.
/// </summary>
public sealed class CharacterFolder
{
    private CharacterFolder(string path, CharacterTitles titles)
    {
        Path = path;
        Id = System.IO.Path.GetFileName(path);
        Titles = titles;
        Books = Enumerable.Range(1, MacroFileNaming.BooksPerCharacter)
            .Select(n => new BookInfo(n, this))
            .ToArray();
    }

    public string Path { get; }

    /// <summary>Folder name, e.g. <c>a1b2c3d</c>. Not readable — hence <see cref="DisplayName"/>.</summary>
    public string Id { get; }

    /// <summary>Name the user attached to this folder, persisted in the settings file.</summary>
    public string? DisplayName { get; set; }

    /// <summary>What to show in the tree: the readable name when there is one, the folder id otherwise.</summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Id : $"{DisplayName} ({Id})";

    /// <summary>False when the folder name is not hexadecimal — unusual, but not a reason to hide it.</summary>
    public bool HasHexId => IsHexId(Id);

    public CharacterTitles Titles { get; }

    /// <summary>All 40 books, whether or not any of their sets exist on disk.</summary>
    public BookInfo[] Books { get; }

    /// <summary>Macro set files actually present.</summary>
    public int SetFileCount => Books.Sum(b => b.SetCount);

    public int BookCount => Books.Count(b => b.Exists);

    /// <summary>Most recent macro write, for spotting the character currently being played.</summary>
    public DateTime LastWriteUtc { get; private set; }

    /// <summary>Files named <c>mcr*.dat</c> that do not match the game's naming and were skipped.</summary>
    public IReadOnlyList<string> SkippedFiles { get; private set; } = [];

    /// <summary>Book 1..40.</summary>
    public BookInfo Book(int number)
    {
        if (number is < 1 or > MacroFileNaming.BooksPerCharacter)
            throw new ArgumentOutOfRangeException(nameof(number), number,
                $"Book must be 1..{MacroFileNaming.BooksPerCharacter}.");
        return Books[number - 1];
    }

    /// <summary>Set file by raw index 0..399.</summary>
    public MacroSetInfo SetByFileIndex(int fileIndex) =>
        Book(MacroFileNaming.BookOf(fileIndex)).Set(MacroFileNaming.SetOf(fileIndex));

    /// <summary>True when a folder holds macro files — the test used to spot characters inside <c>USER</c>.</summary>
    public static bool LooksLikeCharacterFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            if (File.Exists(System.IO.Path.Combine(path, BookTitleSet.PrimaryFileName)))
                return true;

            return Directory.EnumerateFiles(path, MacroFileNaming.SearchPattern)
                .Any(f => MacroFileNaming.TryParseFileName(f, out _));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsHexId(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 16 && name.All(Uri.IsHexDigit);

    /// <summary>
    /// Indexes a character folder: which set files exist, when they were written, and which
    /// <c>mcr*.dat</c>-looking files were ignored.
    /// </summary>
    public static CharacterFolder Scan(string path, IMacroLog? log = null)
    {
        var character = new CharacterFolder(path, CharacterTitles.Load(path, log));
        var skipped = new List<string>();
        DateTime lastWrite = DateTime.MinValue;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(path, MacroFileNaming.SearchPattern).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MacroFileException($"Could not list the character folder: {ex.Message}", ex) { Path = path };
        }

        foreach (string file in files)
        {
            string name = System.IO.Path.GetFileName(file);
            if (!MacroFileNaming.TryParseFileName(name, out int index))
            {
                skipped.Add(name);
                log.Warn($"{path}: ignoring '{name}' — not mcr#.dat.");
                continue;
            }

            var info = character.SetByFileIndex(index);
            var file_ = new FileInfo(file);
            info.Exists = true;
            info.SizeBytes = file_.Length;
            info.LastWriteUtc = file_.LastWriteTimeUtc;

            if (!info.HasExpectedSize)
                log.Warn($"{file}: {info.SizeBytes} bytes instead of {MacroBookFile.FileSize}; it will not load.");

            if (info.LastWriteUtc > lastWrite)
                lastWrite = info.LastWriteUtc;
        }

        character.LastWriteUtc = lastWrite;
        character.SkippedFiles = skipped;
        log.Debug($"{path}: {character.SetFileCount} set file(s) across {character.BookCount} book(s).");

        return character;
    }

    public override string ToString() => $"{Label} — {SetFileCount} set file(s)";
}
