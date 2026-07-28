using FfxiMacros.Core.Model;
using FfxiMacros.Core.Text;

namespace FfxiMacros.Core.Io;

/// <summary>
/// Reads and writes <c>mcr*.dat</c> files.
/// </summary>
/// <remarks>
/// Layout, confirmed byte for byte against 493 real files:
/// <code>
/// 0      8     Version stamp        (copied through untouched)
/// 8     16     MD5 of the data block (recomputed on every save)
/// 24  7600     20 macros x 380 bytes
///
/// per macro:
/// 0      4     reserved / flags     (always 00 00 00 00; copied through)
/// 4     61     line 1               (FFXI text, NUL-padded)
/// ...          lines 2..6
/// 370    9     name                 (FFXI text, NUL-padded)
/// 379    1     reserved             (always 00; copied through)
/// </code>
/// </remarks>
public static class MacroBookFile
{
    public const int MacroCount = MacroBook.MacroCount;   // 20
    public const int LineCount = Macro.LineCount;         // 6

    public const int MacroHeaderSize = 4;
    public const int LineFieldSize = 61;
    public const int NameFieldSize = 9;
    public const int MacroTrailerSize = 1;

    public const int MacroSize = MacroHeaderSize + (LineCount * LineFieldSize) + NameFieldSize + MacroTrailerSize; // 380
    public const int DataSize = MacroCount * MacroSize;                                                            // 7600
    public const int FileSize = FfxiContainer.HeaderSize + DataSize;                                                // 7624

    private const int NameOffset = MacroHeaderSize + (LineCount * LineFieldSize);   // 370
    private const int TrailerOffset = NameOffset + NameFieldSize;                   // 379

    /// <summary>Maximum text bytes in a macro line (the 61st byte is always the NUL terminator).</summary>
    public static int MaxLineBytes => FfxiText.MaxTextBytes(LineFieldSize);   // 60

    /// <summary>Maximum text bytes in a macro name.</summary>
    public static int MaxNameBytes => FfxiText.MaxTextBytes(NameFieldSize);   // 8

    public static MacroBook Load(string path)
    {
        byte[] raw = LongPath.ReadAllBytes(path);
        try
        {
            var book = Read(raw);
            book.SourcePath = LongPath.ForDisplay(path);
            return book;
        }
        catch (MacroFileException ex)
        {
            throw new MacroFileException(ex.Message, ex) { Path = LongPath.ForDisplay(path) };
        }
    }

    public static MacroBook Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != FileSize)
            throw new MacroFileException(
                $"Not a macro book: {raw.Length} bytes, expected exactly {FileSize}.");

        var (version, data, digestValid) = FfxiContainer.Read(raw, DataSize, "macro book");

        var book = new MacroBook { Version = version, DigestWasValid = digestValid };

        for (int i = 0; i < MacroCount; i++)
        {
            ReadOnlySpan<byte> record = data.AsSpan(i * MacroSize, MacroSize);
            var macro = book.Macros[i];

            record[..MacroHeaderSize].CopyTo(macro.Header);
            for (int line = 0; line < LineCount; line++)
                macro.Lines[line] = FfxiText.Decode(record.Slice(MacroHeaderSize + (line * LineFieldSize), LineFieldSize));
            macro.Name = FfxiText.Decode(record.Slice(NameOffset, NameFieldSize));
            macro.Trailer = record[TrailerOffset];
        }

        return book;
    }

    /// <summary>Serialises a book to the exact 7624-byte on-disk form, recomputing the MD5.</summary>
    /// <param name="book">Book to serialise.</param>
    /// <param name="truncate">Cut over-long names and lines instead of throwing.</param>
    public static byte[] ToBytes(MacroBook book, bool truncate = false)
    {
        ArgumentNullException.ThrowIfNull(book);

        var data = new byte[DataSize];

        for (int i = 0; i < MacroCount; i++)
        {
            var macro = book.Macros[i]
                ?? throw new MacroFileException($"Macro slot {i} ({MacroSlot.Describe(i)}) is null.");
            Span<byte> record = data.AsSpan(i * MacroSize, MacroSize);

            if (macro.Header.Length != MacroHeaderSize)
                throw new MacroFileException(
                    $"Macro {MacroSlot.Describe(i)} has a {macro.Header.Length}-byte header, expected {MacroHeaderSize}.");
            macro.Header.CopyTo(record);

            if (macro.Lines.Length != LineCount)
                throw new MacroFileException(
                    $"Macro {MacroSlot.Describe(i)} has {macro.Lines.Length} lines, expected exactly {LineCount}.");

            for (int line = 0; line < LineCount; line++)
            {
                byte[] encoded = EncodeField(macro.Lines[line] ?? "", LineFieldSize, truncate,
                    $"line {line + 1} of macro {MacroSlot.Describe(i)}");
                encoded.CopyTo(record[(MacroHeaderSize + (line * LineFieldSize))..]);
            }

            byte[] name = EncodeField(macro.Name ?? "", NameFieldSize, truncate,
                $"the name of macro {MacroSlot.Describe(i)}");
            name.CopyTo(record[NameOffset..]);

            record[TrailerOffset] = macro.Trailer;
        }

        // The original tool refuses to save a short data block; keep that guard rail.
        if (data.Length != DataSize)
            throw new MacroFileException($"Data has only {data.Length} bytes out of {DataSize}. Not saving!");

        return FfxiContainer.Write(book.Version, data);
    }

    /// <summary>Writes a book to disk atomically.</summary>
    public static void Save(MacroBook book, string path, bool truncate = false)
    {
        byte[] raw = ToBytes(book, truncate);
        if (raw.Length != FileSize)
            throw new MacroFileException($"Refusing to write a {raw.Length}-byte macro book, expected {FileSize}.");

        LongPath.WriteAllBytesAtomic(path, raw);
        book.SourcePath = LongPath.ForDisplay(path);
        book.DigestWasValid = true;
    }

    private static byte[] EncodeField(string text, int fieldSize, bool truncate, string what)
    {
        try
        {
            return FfxiText.Encode(text, fieldSize, truncate);
        }
        catch (FfxiTextException ex)
        {
            throw new MacroFileException($"Cannot encode {what}: {ex.Message}", ex);
        }
    }
}
