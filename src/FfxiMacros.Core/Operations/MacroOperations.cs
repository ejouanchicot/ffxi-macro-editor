using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;

namespace FfxiMacros.Core.Operations;

/// <summary>
/// Copying and moving macros, sets and books — including between characters, keeping the
/// <c>.dat</c> files and the <c>.ttl</c> titles in step.
/// </summary>
public static class MacroOperations
{
    // ---------------------------------------------------------------- macros, in memory

    /// <summary>Overwrites <paramref name="target"/> with a copy of <paramref name="source"/>.</summary>
    public static void CopyMacro(Macro source, Macro target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(source, target))
            return;

        source.Header.CopyTo(target.Header, 0);
        target.Trailer = source.Trailer;
        target.Name = source.Name;
        for (int i = 0; i < Macro.LineCount; i++)
            target.Lines[i] = source.Lines[i];
    }

    /// <summary>Copies <paramref name="source"/> onto <paramref name="target"/>, then empties the source.</summary>
    public static void MoveMacro(Macro source, Macro target)
    {
        if (ReferenceEquals(source, target))
            return;

        CopyMacro(source, target);
        source.Clear();
    }

    public static void SwapMacros(Macro a, Macro b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (ReferenceEquals(a, b))
            return;

        var spare = a.Clone();
        CopyMacro(b, a);
        CopyMacro(spare, b);
    }

    // ---------------------------------------------------------------- sets, on disk

    /// <summary>
    /// Copies a set file byte for byte, so the version stamp and every reserved byte reach the
    /// target exactly as the game wrote them.
    /// </summary>
    public static void CopySet(MacroSetInfo source, MacroSetInfo target, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (SamePath(source, target))
            throw new MacroFileException("A set cannot be copied onto itself.") { Path = source.FullPath };
        if (!source.Exists)
            throw new MacroFileException($"{source.FileName} does not exist.") { Path = source.FullPath };

        byte[] raw = LongPath.ReadAllBytes(source.FullPath);
        if (raw.Length != MacroBookFile.FileSize)
            throw new MacroFileException(
                $"{source.FileName} is {raw.Length} bytes instead of {MacroBookFile.FileSize}; refusing to copy it.")
            { Path = source.FullPath };

        LongPath.WriteAllBytesAtomic(target.FullPath, raw);
        target.Refresh();
        log.Info($"Copied {source.FullPath} to {target.FullPath}.");
    }

    /// <summary>Copies a set, then deletes the original.</summary>
    public static void MoveSet(MacroSetInfo source, MacroSetInfo target, IMacroLog? log = null)
    {
        if (SamePath(source, target))
            return;

        CopySet(source, target, log);
        DeleteSet(source, log);
    }

    public static void DeleteSet(MacroSetInfo set, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!set.Exists)
            return;

        try
        {
            File.Delete(LongPath.Normalize(set.FullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MacroFileException($"Could not delete the file: {ex.Message}", ex) { Path = set.FullPath };
        }

        set.Refresh();
        log.Info($"Deleted {set.FullPath}.");
    }

    // ---------------------------------------------------------------- books, on disk

    /// <summary>
    /// Copies a whole book: its ten set files and its title. Target sets with no counterpart in the
    /// source are deleted, so the target ends up as a faithful copy rather than a mixture.
    /// </summary>
    /// <param name="keepTargetTitle">Leave the target's own title alone instead of copying the source's.</param>
    public static void CopyBook(BookInfo source, BookInfo target, bool keepTargetTitle = false, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (SameBook(source, target))
            throw new MacroFileException("A book cannot be copied onto itself.");

        for (int set = 1; set <= MacroFileNaming.SetsPerBook; set++)
        {
            var from = source.Set(set);
            var to = target.Set(set);

            if (from.Exists)
                CopySet(from, to, log);
            else if (to.Exists)
                DeleteSet(to, log);
        }

        if (!keepTargetTitle)
        {
            target.Title = source.Title;
            target.Character.Titles.SaveHalfFor(target.Number);
        }

        log.Info($"Copied book {source.Number} of {source.Character.Id} to book {target.Number} of {target.Character.Id}.");
    }

    /// <summary>Copies a book, then clears the source: its set files are deleted and its title reset.</summary>
    public static void MoveBook(BookInfo source, BookInfo target, IMacroLog? log = null)
    {
        if (SameBook(source, target))
            return;

        CopyBook(source, target, keepTargetTitle: false, log);

        for (int set = 1; set <= MacroFileNaming.SetsPerBook; set++)
            DeleteSet(source.Set(set), log);

        source.Title = "";
        source.Character.Titles.SaveHalfFor(source.Number);
        log.Info($"Moved book {source.Number} of {source.Character.Id} to book {target.Number} of {target.Character.Id}.");
    }

    /// <summary>Deletes every set file of a book and clears its title.</summary>
    public static void ClearBook(BookInfo book, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(book);

        for (int set = 1; set <= MacroFileNaming.SetsPerBook; set++)
            DeleteSet(book.Set(set), log);

        book.Title = "";
        book.Character.Titles.SaveHalfFor(book.Number);
        log.Info($"Cleared book {book.Number} of {book.Character.Id}.");
    }

    /// <summary>Renames a book and writes the half of the title file that holds it.</summary>
    public static void RenameBook(BookInfo book, string title, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(title);

        if (!Text.FfxiText.Fits(title, BookTitleSet.TitleFieldSize))
            throw new MacroFileException(
                $"'{title}' does not fit in a book title ({BookTitleSet.MaxTitleBytes} bytes maximum).");

        book.Title = title;
        book.Character.Titles.SaveHalfFor(book.Number);
        log.Info($"Book {book.Number} of {book.Character.Id} renamed to '{title}'.");
    }

    private static bool SamePath(MacroSetInfo a, MacroSetInfo b) =>
        string.Equals(Path.GetFullPath(a.FullPath), Path.GetFullPath(b.FullPath), StringComparison.OrdinalIgnoreCase);

    private static bool SameBook(BookInfo a, BookInfo b) =>
        a.Number == b.Number
        && string.Equals(Path.GetFullPath(a.Character.Path), Path.GetFullPath(b.Character.Path), StringComparison.OrdinalIgnoreCase);
}
