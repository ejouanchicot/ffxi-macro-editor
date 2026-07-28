using System.Globalization;
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

    public static string Export(MacroBook book, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(book);

        var sb = new StringBuilder();
        sb.AppendLine("# FFXI macro set");
        if (!string.IsNullOrWhiteSpace(title))
            sb.AppendLine(CultureInfo.InvariantCulture, $"# {title}");
        sb.AppendLine();

        bool any = false;
        for (int index = 0; index < MacroBook.MacroCount; index++)
        {
            var macro = book.Macros[index];
            if (macro.IsEmpty)
                continue;

            any = true;
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{MacroSlot.Describe(index)}] {QuoteName(macro.Name)}");

            // Written up to the last non-empty line, gaps included: a macro may leave line 2 empty
            // and still use line 3, and dropping the gap would shift the commands up.
            for (int line = 0; line <= LastUsedLine(macro); line++)
                sb.AppendLine(macro.Lines[line]);

            sb.AppendLine();
        }

        if (!any)
            sb.AppendLine("# (aucune macro)");

        return sb.ToString();
    }

    /// <summary>
    /// Reads a set back. Slots absent from the text are left untouched in
    /// <paramref name="into"/>, so a partial file can be merged into an existing set.
    /// </summary>
    /// <exception cref="MacroFileException">A slot header is malformed or a macro has too many lines.</exception>
    public static MacroBook Import(string text, MacroBook? into = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var book = into ?? new MacroBook { Version = 1 };

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

                current = ParseSlot(line[1..close], number);
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
        return book;
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
