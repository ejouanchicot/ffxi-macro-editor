using System.Text.RegularExpressions;
using FfxiMacros.Core.Model;

namespace FfxiMacros.Core.Operations;

/// <param name="MacroIndex">0-based macro slot.</param>
/// <param name="LineIndex">0-based line, or -1 for the macro name.</param>
/// <param name="Before">Text as it is now.</param>
/// <param name="After">Text the repair would write.</param>
/// <param name="Reason">Why it is broken, in words.</param>
public sealed record MacroRepairSuggestion(int MacroIndex, int LineIndex, string Before, string After, string Reason)
{
    public string Where =>
        LineIndex < 0 ? $"{MacroSlot.Describe(MacroIndex)} · nom" : $"{MacroSlot.Describe(MacroIndex)} · ligne {LineIndex + 1}";
}

/// <summary>
/// Finds and fixes macro text the game cannot run.
/// </summary>
/// <remarks>
/// The 2014 editor wrote a NUL byte over the leading <c>/</c> of some lines, and left the tail of a
/// previously longer line after the terminator. FFXI stops reading at the first NUL, so those lines
/// silently do nothing in game. 52 such lines were found in the reference corpus.
/// </remarks>
public static class MacroRepair
{
    private const string NulEscape = "{00}";

    /// <summary>
    /// What FFXI actually shows for a field: it stops reading at the first NUL, so
    /// <c>Palis{00}el</c> appears in game as <c>Palis</c> and <c>{00}con send …</c> as nothing at all.
    /// </summary>
    public static string VisibleInGame(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        int nul = text.IndexOf(NulEscape, StringComparison.Ordinal);
        return nul < 0 ? text : text[..nul];
    }

    /// <summary>True when the stored text holds more than the game will ever read.</summary>
    public static bool IsDamaged(string text) =>
        !string.IsNullOrEmpty(text) && text.Contains(NulEscape, StringComparison.Ordinal);

    /// <summary>
    /// Replaces a word wherever it appears in macro text, without touching longer words that
    /// merely start the same way.
    /// </summary>
    /// <remarks>
    /// The distinction matters: a character named <c>Sylvane</c> mistyped as <c>Kaorie</c> must be
    /// fixed, but a plain replace would then turn every correct <c>Sylvane</c> into <c>Sylvanes</c>.
    /// Word boundaries are only required on the sides that end in a word character, so
    /// <c>&lt;lastid&gt;</c> — wrapped in angle brackets — is matched literally.
    /// </remarks>
    public static string Substitute(string text, string search, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(search);
        ArgumentNullException.ThrowIfNull(replacement);

        string pattern =
            (char.IsLetterOrDigit(search[0]) || search[0] == '_' ? @"\b" : "")
            + Regex.Escape(search)
            + (char.IsLetterOrDigit(search[^1]) || search[^1] == '_' ? @"\b" : "");

        return Regex.Replace(text, pattern, replacement.Replace("$", "$$", StringComparison.Ordinal));
    }

    /// <summary>Lists what is broken in a book, without changing anything.</summary>
    public static IReadOnlyList<MacroRepairSuggestion> Inspect(MacroBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var suggestions = new List<MacroRepairSuggestion>();

        for (int index = 0; index < MacroBook.MacroCount; index++)
        {
            var macro = book.Macros[index];

            for (int line = 0; line < Macro.LineCount; line++)
            {
                if (TryRepair(macro.Lines[line], out string repaired, out string reason))
                    suggestions.Add(new MacroRepairSuggestion(index, line, macro.Lines[line], repaired, reason));
            }

            if (TryRepair(macro.Name, out string name, out string nameReason))
                suggestions.Add(new MacroRepairSuggestion(index, -1, macro.Name, name, nameReason));
        }

        return suggestions;
    }

    /// <summary>Applies every suggestion. Returns how many fields were changed.</summary>
    public static int Repair(MacroBook book)
    {
        var suggestions = Inspect(book);

        foreach (var suggestion in suggestions)
        {
            if (suggestion.LineIndex < 0)
                book.Macros[suggestion.MacroIndex].Name = suggestion.After;
            else
                book.Macros[suggestion.MacroIndex].Lines[suggestion.LineIndex] = suggestion.After;
        }

        return suggestions.Count;
    }

    /// <summary>
    /// Works out what a damaged field was meant to say: a leading NUL stood in for the <c>/</c> that
    /// opens every command, and anything after a later NUL is the tail of an older, longer line.
    /// </summary>
    public static bool TryRepair(string text, out string repaired, out string reason)
    {
        repaired = text;
        reason = "";
        if (string.IsNullOrEmpty(text) || !text.Contains(NulEscape, StringComparison.Ordinal))
            return false;

        string result = text;
        var reasons = new List<string>();

        if (result.StartsWith(NulEscape, StringComparison.Ordinal))
        {
            result = "/" + result[NulEscape.Length..];
            reasons.Add("octet nul à la place du « / » initial");
        }

        int trailing = result.IndexOf(NulEscape, StringComparison.Ordinal);
        if (trailing >= 0)
        {
            result = result[..trailing];
            reasons.Add("reste d'une ligne plus longue après le terminateur");
        }

        result = result.TrimEnd();

        // "/" on its own is not a command: a field holding nothing but the stray NUL was already
        // empty as far as the game is concerned, so there is nothing to repair.
        if (result.Length <= 1 || result == text)
            return false;

        repaired = result;
        reason = string.Join(", ", reasons);
        return true;
    }
}
