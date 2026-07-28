using System.Globalization;

namespace FfxiMacros.Core.Io;

/// <summary>
/// Maps between <c>mcr*.dat</c> file names and the book/set coordinates the game shows.
/// </summary>
/// <remarks>
/// A character folder holds up to 400 macro files: <c>mcr.dat</c> is index 0, then <c>mcr1.dat</c>
/// through <c>mcr399.dat</c>. Ten consecutive files make one book, so index = (book - 1) * 10 + (set - 1),
/// giving 40 books of 10 sets of 20 macros. Confirmed on a 400-file character folder: files cluster in
/// tens (140-149, 190-199) and the two <c>.ttl</c> files hold exactly 40 titles between them.
/// Files are created lazily by the game, so most folders hold far fewer.
/// </remarks>
public static class MacroFileNaming
{
    public const int SetsPerBook = 10;
    public const int BooksPerCharacter = 40;
    public const int FileCount = BooksPerCharacter * SetsPerBook;   // 400

    public const string FirstFileName = "mcr.dat";
    public const string SearchPattern = "mcr*.dat";

    /// <summary>File name for a raw file index (0..399).</summary>
    public static string FileName(int fileIndex)
    {
        ValidateIndex(fileIndex);
        return fileIndex == 0 ? FirstFileName : $"mcr{fileIndex.ToString(CultureInfo.InvariantCulture)}.dat";
    }

    /// <summary>File name for 1-based book and set numbers.</summary>
    public static string FileName(int book, int set) => FileName(FileIndex(book, set));

    public static int FileIndex(int book, int set)
    {
        if (book is < 1 or > BooksPerCharacter)
            throw new ArgumentOutOfRangeException(nameof(book), book, $"Book must be 1..{BooksPerCharacter}.");
        if (set is < 1 or > SetsPerBook)
            throw new ArgumentOutOfRangeException(nameof(set), set, $"Set must be 1..{SetsPerBook}.");

        return ((book - 1) * SetsPerBook) + (set - 1);
    }

    /// <summary>1-based book number holding <paramref name="fileIndex"/>.</summary>
    public static int BookOf(int fileIndex)
    {
        ValidateIndex(fileIndex);
        return (fileIndex / SetsPerBook) + 1;
    }

    /// <summary>1-based set number within its book.</summary>
    public static int SetOf(int fileIndex)
    {
        ValidateIndex(fileIndex);
        return (fileIndex % SetsPerBook) + 1;
    }

    /// <summary>
    /// Parses a macro file name. Rejects anything that is not exactly <c>mcr.dat</c> or <c>mcr&lt;n&gt;.dat</c>
    /// so that neighbours such as <c>mcr.sys</c> or <c>mcrx.dat</c> are skipped rather than mangled.
    /// </summary>
    public static bool TryParseFileName(string fileName, out int fileIndex)
    {
        fileIndex = -1;
        if (string.IsNullOrEmpty(fileName))
            return false;

        string name = Path.GetFileName(fileName);
        if (!name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!name.StartsWith("mcr", StringComparison.OrdinalIgnoreCase))
            return false;

        string digits = name[3..^4];
        if (digits.Length == 0)
        {
            fileIndex = 0;
            return true;
        }

        // No leading zeros, no sign, no spaces: the game writes mcr7.dat, never mcr07.dat.
        if (digits.Length > 1 && digits[0] == '0')
            return false;
        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            return false;
        if (parsed is < 0 or >= FileCount)
            return false;

        fileIndex = parsed;
        return true;
    }

    /// <summary>Label such as <c>Book 15 / Set 3</c>.</summary>
    public static string Describe(int fileIndex) =>
        $"Book {BookOf(fileIndex)} / Set {SetOf(fileIndex)}";

    private static void ValidateIndex(int fileIndex)
    {
        if (fileIndex is < 0 or >= FileCount)
            throw new ArgumentOutOfRangeException(nameof(fileIndex), fileIndex, $"File index must be 0..{FileCount - 1}.");
    }
}
