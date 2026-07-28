using FfxiMacros.Core.Text;

namespace FfxiMacros.Core.Io;

/// <summary>
/// Book titles, stored outside the <c>.dat</c> files in <c>mcr.ttl</c> (books 1-20) and
/// <c>mcr_2.ttl</c> (books 21-40).
/// </summary>
/// <remarks>
/// Same 24-byte header as a macro book, followed by 20 fixed 16-byte name fields (344 bytes total).
/// FFXI writes the placeholders <c>Book01</c>..<c>Book40</c> for untitled books.
/// </remarks>
public sealed class BookTitleSet
{
    public const int TitleCount = 20;
    public const int TitleFieldSize = 16;
    public const int DataSize = TitleCount * TitleFieldSize;                     // 320
    public const int FileSize = FfxiContainer.HeaderSize + DataSize;             // 344

    public const string PrimaryFileName = "mcr.ttl";
    public const string SecondaryFileName = "mcr_2.ttl";

    /// <summary>Maximum text bytes in a title.</summary>
    public static int MaxTitleBytes => FfxiText.MaxTextBytes(TitleFieldSize);    // 15

    public ulong Version { get; set; }

    public string[] Titles { get; } = new string[TitleCount];

    public string? SourcePath { get; set; }

    public bool DigestWasValid { get; set; } = true;

    /// <summary>False for <c>mcr.ttl</c> (books 1-20), true for <c>mcr_2.ttl</c> (books 21-40).</summary>
    public bool IsSecondary { get; set; }

    public BookTitleSet()
    {
        for (int i = 0; i < TitleCount; i++)
            Titles[i] = "";
    }

    /// <summary>1-based book number of the title at <paramref name="index"/>, accounting for the file half.</summary>
    public int BookNumberAt(int index) => (IsSecondary ? TitleCount : 0) + index + 1;

    /// <summary>The placeholder FFXI itself writes for an untitled book, e.g. <c>Book07</c>.</summary>
    public static string DefaultTitle(int bookNumber) => $"Book{bookNumber:D2}";

    public static BookTitleSet Load(string path)
    {
        byte[] raw = LongPath.ReadAllBytes(path);
        try
        {
            var set = Read(raw);
            set.SourcePath = LongPath.ForDisplay(path);
            set.IsSecondary = System.IO.Path.GetFileName(path)
                .Equals(SecondaryFileName, StringComparison.OrdinalIgnoreCase);
            return set;
        }
        catch (MacroFileException ex)
        {
            throw new MacroFileException(ex.Message, ex) { Path = LongPath.ForDisplay(path) };
        }
    }

    public static BookTitleSet Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != FileSize)
            throw new MacroFileException($"Not a book title file: {raw.Length} bytes, expected exactly {FileSize}.");

        var (version, data, digestValid) = FfxiContainer.Read(raw, DataSize, "book title");

        var set = new BookTitleSet { Version = version, DigestWasValid = digestValid };
        for (int i = 0; i < TitleCount; i++)
            set.Titles[i] = FfxiText.Decode(data.AsSpan(i * TitleFieldSize, TitleFieldSize));

        return set;
    }

    public byte[] ToBytes(bool truncate = false)
    {
        var data = new byte[DataSize];
        for (int i = 0; i < TitleCount; i++)
        {
            byte[] encoded;
            try
            {
                encoded = FfxiText.Encode(Titles[i] ?? "", TitleFieldSize, truncate);
            }
            catch (FfxiTextException ex)
            {
                throw new MacroFileException(
                    $"Cannot encode the title of book {BookNumberAt(i)}: {ex.Message}", ex);
            }
            encoded.CopyTo(data.AsSpan(i * TitleFieldSize));
        }

        return FfxiContainer.Write(Version, data);
    }

    public void Save(string path, bool truncate = false)
    {
        byte[] raw = ToBytes(truncate);
        if (raw.Length != FileSize)
            throw new MacroFileException($"Refusing to write a {raw.Length}-byte title file, expected {FileSize}.");

        LongPath.WriteAllBytesAtomic(path, raw);
        SourcePath = LongPath.ForDisplay(path);
        DigestWasValid = true;
    }
}
