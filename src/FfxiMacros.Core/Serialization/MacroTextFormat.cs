using System.Globalization;
using FfxiMacros.Core.Text;
using System.Text;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;

namespace FfxiMacros.Core.Serialization;

/// <summary>
/// A readable text form of a macro set, for sharing a build or keeping macros in version control.
/// </summary>
/// <remarks>
/// <para>The format is deliberately plain, and re-imports into the exact same bytes:</para>
/// <code>
/// # FFXI macro set
/// # book: 15 (PldRunR)
/// # set: 1
///
/// [Ctrl-1] ShieldBa
/// /ja "{AT:Shield Bash}" &lt;stnpc&gt;
///
/// [Alt-3] Fealty
/// /ja "Fealty" &lt;me&gt;
/// </code>
/// <para>
/// Lines starting with <c>#</c> are comments. A slot header opens a macro; the lines that follow are
/// its command lines, in order. Empty slots are simply left out.
/// </para>
/// </remarks>
public static class MacroTextFormat
{
    public const string FileExtension = ".txt";

    private const string CommentPrefix = "#";

    /// <summary>Opens a set inside a multi-set file: <c>[Set 3]</c>.</summary>
    private const string SetPrefix = "Set";

    public static string Export(MacroBook book, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        return Export([new MacroSetExport(0, book)], title);
    }

    /// <summary>
    /// Writes several sets into one file — a whole book, in the game's sense of the word.
    /// </summary>
    /// <remarks>
    /// Each one opens with a <c>[Set 3]</c> header. A set numbered 0 writes no header at all, which
    /// is what a single-set export looks like and what older files already are.
    /// </remarks>
    public static string Export(IReadOnlyList<MacroSetExport> sets, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var body = new StringBuilder();
        bool anyMacro = false;
        bool quotedAName = false;

        foreach (var set in sets)
        {
            if (set.SetNumber > 0)
            {
                body.AppendLine(CultureInfo.InvariantCulture, $"[{SetPrefix} {set.SetNumber}]");
                body.AppendLine();
            }

            bool anyHere = false;
            for (int index = 0; index < MacroBook.MacroCount; index++)
            {
                var macro = set.Book.Macros[index];
                if (macro.IsEmpty)
                    continue;

                anyHere = anyMacro = true;
                string name = QuoteName(macro.Name);
                quotedAName |= name.StartsWith('"');
                body.AppendLine(CultureInfo.InvariantCulture, $"[{MacroSlot.Describe(index)}] {name}");

                // Written up to the last non-empty line, gaps included: a macro may leave line 2
                // empty and still use line 3, and dropping the gap would shift the commands up.
                for (int line = 0; line <= LastUsedLine(macro); line++)
                    body.AppendLine(macro.Lines[line]);

                body.AppendLine();
            }

            if (!anyHere && set.SetNumber > 0)
                body.AppendLine("# (empty)").AppendLine();
        }

        if (!anyMacro && sets.Count <= 1)
            body.AppendLine("# (no macro)");

        var sb = new StringBuilder();
        sb.AppendLine("# FFXI macro set");
        if (!string.IsNullOrWhiteSpace(title))
            sb.AppendLine(CultureInfo.InvariantCulture, $"# {title}");

        AppendLegend(sb, body.ToString(), quotedAName);
        sb.AppendLine();
        sb.Append(body);

        return sb.ToString();
    }

    /// <summary>
    /// Explains the three notations this format uses, and only the ones the file actually contains:
    /// a set with no auto-translate phrase should not be handed a paragraph about them.
    /// </summary>
    /// <remarks>
    /// The <c>{00}</c> note earns its place. A field whose first byte is null reads as empty in the
    /// editor, because that is what the game makes of it — but the export has to carry the bytes to
    /// round-trip, so the line reappears here and would otherwise look like a mystery.
    /// </remarks>
    private static void AppendLegend(StringBuilder sb, string body, bool quotedAName)
    {
        var notes = new List<string>();

        if (body.Contains(FfxiText.PhraseOpen, StringComparison.Ordinal))
            notes.Add("«Provoke»  an auto-translate phrase — six bytes in the file, whatever its length");

        if (body.Contains("{00}", StringComparison.Ordinal))
            notes.Add("{00}       a byte the game stops reading at — that line does nothing in game");

        if (quotedAName)
            notes.Add("\"Box \"     a name whose spacing counts, quoted so a text editor cannot eat it");

        if (notes.Count == 0)
            return;

        sb.AppendLine("#");
        foreach (string note in notes)
            sb.AppendLine(CultureInfo.InvariantCulture, $"#   {note}");
    }

    /// <summary>
    /// Reads a set back. Slots absent from the text are left untouched in
    /// <paramref name="into"/>, so a partial file can be merged into an existing set.
    /// </summary>
    /// <exception cref="MacroFileException">A slot header is malformed or a macro has too many lines.</exception>
    public static MacroBook Import(string text, MacroBook? into = null)
    {
        var sets = ImportSets(text, into);
        return sets[0].Book;
    }

    /// <summary>
    /// Reads every set a file holds. A file with no <c>[Set 3]</c> header yields a single entry
    /// numbered 0, which is what the caller should read as "whichever set you were pointing at".
    /// </summary>
    /// <param name="text">The exported text.</param>
    /// <param name="into">
    /// Where the first set lands. Later sets always get a fresh book, since one destination cannot
    /// hold ten of them.
    /// </param>
    /// <exception cref="MacroFileException">The text is malformed.</exception>
    public static IReadOnlyList<MacroSetExport> ImportSets(string text, MacroBook? into = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<MacroSetExport>();
        var book = into ?? new MacroBook { Version = 1 };
        int setNumber = 0;

        int current = -1;
        int number = 0;
        var pending = new List<string>();

        // Blank lines are buffered rather than acted on straight away: a blank line inside a macro is
        // a genuinely empty command line and must keep its position, while the blank line that
        // separates two macros in the export is only there for readability.
        void Flush(int lastLineNumber)
        {
            if (current < 0)
                return;

            while (pending.Count > 0 && pending[^1].Length == 0)
                pending.RemoveAt(pending.Count - 1);

            if (pending.Count > Macro.LineCount)
                throw new MacroFileException(
                    $"Line {lastLineNumber}: macro {MacroSlot.Describe(current)} has {pending.Count} lines, " +
                    $"{Macro.LineCount} maximum.");

            for (int i = 0; i < pending.Count; i++)
                book.Macros[current].Lines[i] = pending[i];

            pending.Clear();
        }

        foreach (string raw in text.Split('\n'))
        {
            number++;
            string line = raw.TrimEnd('\r');

            if (line.TrimStart().StartsWith(CommentPrefix, StringComparison.Ordinal))
                continue;

            if (line.StartsWith('['))
            {
                Flush(number);

                int close = line.IndexOf(']');
                if (close < 0)
                    throw new MacroFileException($"Line {number}: '[' with no matching ']'.");

                string inside = line[1..close].Trim();
                if (inside.StartsWith(SetPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(new MacroSetExport(setNumber, book));
                    setNumber = ParseSetNumber(inside[SetPrefix.Length..], number);
                    book = new MacroBook { Version = 1 };
                    current = -1;
                    continue;
                }

                current = ParseSlot(inside, number);
                book.Macros[current].Clear();
                book.Macros[current].Name = UnquoteName(line[(close + 1)..]);
                continue;
            }

            if (line.Trim().Length == 0)
            {
                if (current >= 0)
                    pending.Add("");
                continue;
            }

            if (current < 0)
                throw new MacroFileException($"Line {number}: a command line before any [Ctrl-1] header.");

            pending.Add(line);
        }

        Flush(number);
        found.Add(new MacroSetExport(setNumber, book));

        // The first entry is the run of macros before any [Set n] header. In a file that opens with
        // one — everything this editor writes for a book — that run is empty and says nothing.
        if (found.Count > 1 && found[0].SetNumber == 0 && found[0].Book.Macros.All(m => m.IsEmpty))
            found.RemoveAt(0);

        return found;
    }

    private static int ParseSetNumber(string text, int lineNumber)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            || number is < 1 or > 10)
        {
            throw new MacroFileException($"Line {lineNumber}: '{text.Trim()}' is not a set number between 1 and 10.");
        }

        return number;
    }

    /// <summary>
    /// Quotes a name whose spacing matters. Real macros do carry names such as <c>"Box "</c>, and a
    /// bare trailing space would not survive a text editor.
    /// </summary>
    private static string QuoteName(string name) =>
        name.Length != name.Trim().Length || name.Contains('"', StringComparison.Ordinal)
            ? "\"" + name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : name;

    private static string UnquoteName(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);

        return trimmed;
    }

    /// <summary>Index of the last line holding text, or -1 when the macro has none.</summary>
    internal static int LastUsedLine(Macro macro)
    {
        for (int line = Macro.LineCount - 1; line >= 0; line--)
        {
            if (macro.Lines[line].Length > 0)
                return line;
        }
        return -1;
    }

    /// <summary>Parses <c>Ctrl-1</c> / <c>Alt-0</c> into a slot index.</summary>
    private static int ParseSlot(string label, int lineNumber)
    {
        string trimmed = label.Trim();
        int dash = trimmed.IndexOf('-');
        if (dash <= 0 || dash == trimmed.Length - 1)
            throw new MacroFileException($"Line {lineNumber}: '{label}' is not a slot such as Ctrl-1 or Alt-0.");

        string palette = trimmed[..dash].Trim();
        string key = trimmed[(dash + 1)..].Trim();

        bool alt = palette.Equals("Alt", StringComparison.OrdinalIgnoreCase);
        if (!alt && !palette.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            throw new MacroFileException($"Line {lineNumber}: unknown palette '{palette}'; expected Ctrl or Alt.");

        if (key.Length != 1 || !char.IsAsciiDigit(key[0]))
            throw new MacroFileException($"Line {lineNumber}: '{key}' is not a key digit 1-9 or 0.");

        int digit = key[0] - '0';
        int position = digit == 0 ? MacroSlot.PaletteSize - 1 : digit - 1;
        return (alt ? MacroSlot.PaletteSize : 0) + position;
    }
}
