using System.Globalization;
using System.IO.Compression;
using System.Text;

using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Discovery;

namespace FfxiMacros.Core.Io;

/// <summary>What an archive turned out to hold, once opened.</summary>
public sealed record ArchiveContents(string Character, int Book, string Title, int SetCount);

/// <summary>
/// A book packed as the game's own files, in a zip.
/// </summary>
/// <remarks>
/// <para>
/// The text and JSON exports are for reading and for sharing a macro with someone; this is for
/// keeping. It holds the <c>mcr*.dat</c> files byte for byte — version stamp, reserved bytes, the
/// lot — plus the book's title, so restoring one puts the game back exactly where it was rather
/// than approximately.
/// </para>
/// <para>
/// A plain zip with the original file names inside, and a short manifest beside them. Anyone can
/// open it, and a set can be pulled out by hand and dropped into a USER folder without this editor.
/// </para>
/// </remarks>
public static class MacroArchive
{
    public const string FileExtension = ".ffxibook.zip";
    private const string ManifestName = "book.txt";

    /// <summary>
    /// Writes every set the book has, plus what is needed to put them back where they belong.
    /// </summary>
    /// <exception cref="MacroFileException">The archive could not be written.</exception>
    public static int Export(BookInfo book, string path, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(book);

        try
        {
            using var stream = new FileStream(LongPath.Normalize(path), FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            int written = 0;
            foreach (var set in book.Sets)
            {
                if (!set.Exists)
                    continue;

                var entry = zip.CreateEntry(set.FileName, CompressionLevel.Optimal);
                using var target = entry.Open();
                target.Write(LongPath.ReadAllBytes(set.FullPath));
                written++;
            }

            Manifest(zip, book, written);
            log.Info($"Exported book {book.Number} of {book.Character.Id} ({written} set(s)) to {path}.");
            return written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new MacroFileException($"Could not write the archive: {ex.Message}", ex)
            { Path = LongPath.ForDisplay(path) };
        }
    }

    /// <summary>
    /// Packs every macro file of every character: the whole library, in one archive.
    /// </summary>
    /// <remarks>
    /// A book at a time is what you want when moving one around. This is what you want before a
    /// reorganisation — four hundred set files and both title files per character, byte for byte,
    /// laid out as <c>&lt;character&gt;/mcr140.dat</c> so a single set can be pulled back out by
    /// hand. It is the same thing the editor copies aside on its own before its first write of a
    /// session, except that this one you asked for and know where to find.
    /// </remarks>
    /// <returns>How many files were written.</returns>
    public static int ExportEverything(IEnumerable<CharacterFolder> characters, string path, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(characters);

        try
        {
            using var stream = new FileStream(LongPath.Normalize(path), FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            int written = 0;

            foreach (var character in characters)
            {
                foreach (var set in character.Books.SelectMany(b => b.Sets).Where(s => s.Exists))
                {
                    Add(zip, $"{character.Id}/{set.FileName}", LongPath.ReadAllBytes(set.FullPath));
                    written++;
                }

                foreach (string titles in new[] { character.Titles.PrimaryPath, character.Titles.SecondaryPath })
                {
                    if (!File.Exists(LongPath.Normalize(titles)))
                        continue;

                    Add(zip, $"{character.Id}/{Path.GetFileName(titles)}", LongPath.ReadAllBytes(titles));
                    written++;
                }
            }

            log.Info($"Exported {written} file(s) to {path}.");
            return written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new MacroFileException($"Could not write the archive: {ex.Message}", ex)
            { Path = LongPath.ForDisplay(path) };
        }
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    /// <summary>What an archive holds, without writing anything.</summary>
    /// <exception cref="MacroFileException">The file is not an archive this editor wrote.</exception>
    public static ArchiveContents Read(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(LongPath.Normalize(path));
            var fields = ManifestOf(zip);

            return new ArchiveContents(
                fields.GetValueOrDefault("character", "?"),
                int.TryParse(fields.GetValueOrDefault("book"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 0,
                fields.GetValueOrDefault("title", ""),
                zip.Entries.Count(e => MacroFileNaming.TryParseFileName(e.Name, out _)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new MacroFileException($"Could not read the archive: {ex.Message}", ex)
            { Path = LongPath.ForDisplay(path) };
        }
    }

    /// <summary>
    /// Puts an archive's sets into a book, and its title with them.
    /// </summary>
    /// <remarks>
    /// The set files are placed by their position in the archived book, not by their original file
    /// names: set 3 of what was exported becomes set 3 of the book it is restored into, whichever
    /// book that is. Sets the archive does not carry are deleted, so the book ends up as a faithful
    /// copy rather than a mixture of the two.
    /// </remarks>
    /// <returns>How many sets were written.</returns>
    public static int Import(string path, BookInfo target, bool keepTargetTitle = false, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            using var zip = ZipFile.OpenRead(LongPath.Normalize(path));
            var fields = ManifestOf(zip);
            int restored = 0;

            for (int number = 1; number <= MacroFileNaming.SetsPerBook; number++)
            {
                var set = target.Set(number);
                var entry = EntryForSet(zip, number);

                if (entry is null)
                {
                    if (set.Exists)
                        Operations.MacroOperations.DeleteSet(set, log);

                    continue;
                }

                byte[] raw = Read(entry);
                if (raw.Length != MacroBookFile.FileSize)
                {
                    throw new MacroFileException(
                        $"{entry.Name} is {raw.Length} bytes instead of {MacroBookFile.FileSize}; refusing to restore it.")
                    { Path = LongPath.ForDisplay(path) };
                }

                LongPath.WriteAllBytesAtomic(set.FullPath, raw);
                set.Refresh();
                restored++;
            }

            if (!keepTargetTitle && fields.TryGetValue("title", out string? title))
            {
                target.Title = title;
                target.Character.Titles.SaveHalfFor(target.Number);
            }

            log.Info($"Restored {restored} set(s) from {path} into book {target.Number} of {target.Character.Id}.");
            return restored;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new MacroFileException($"Could not read the archive: {ex.Message}", ex)
            { Path = LongPath.ForDisplay(path) };
        }
    }

    /// <summary>The entry holding a given set of the archived book, whatever book it came from.</summary>
    private static ZipArchiveEntry? EntryForSet(ZipArchive zip, int setNumber) =>
        zip.Entries.FirstOrDefault(entry =>
            MacroFileNaming.TryParseFileName(entry.Name, out int index)
            && MacroFileNaming.SetOf(index) == setNumber);

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// A few readable lines beside the files: whose book it was, which one, and what it was called.
    /// </summary>
    /// <remarks>
    /// The title is the part that cannot be worked out from the set files, and it is what makes a
    /// restored book look like itself again. The rest is there so a stranger opening the zip in a
    /// year can tell what they are holding.
    /// </remarks>
    private static void Manifest(ZipArchive zip, BookInfo book, int sets)
    {
        var text = new StringBuilder()
            .AppendLine("# FFXI Macro Editor — book archive")
            .AppendLine("# The mcr*.dat files beside this one are the game's own, byte for byte.")
            .AppendLine(CultureInfo.InvariantCulture, $"character={book.Character.Id}")
            .AppendLine(CultureInfo.InvariantCulture, $"name={book.Character.DisplayName ?? ""}")
            .AppendLine(CultureInfo.InvariantCulture, $"book={book.Number}")
            .AppendLine(CultureInfo.InvariantCulture, $"title={book.Title}")
            .AppendLine(CultureInfo.InvariantCulture, $"sets={sets}");

        var entry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static Dictionary<string, string> ManifestOf(ZipArchive zip)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (zip.GetEntry(ManifestName) is not { } entry)
            return fields;

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            int equals = line.IndexOf('=');
            if (equals > 0 && !line.StartsWith('#'))
                fields[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        return fields;
    }
}
