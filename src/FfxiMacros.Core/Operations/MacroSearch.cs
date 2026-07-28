using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;

namespace FfxiMacros.Core.Operations;

/// <summary>Where a search term was found.</summary>
public enum MacroSearchField
{
    BookTitle,
    MacroName,
    MacroLine,
}

/// <param name="Character">Character folder holding the hit.</param>
/// <param name="BookNumber">1-based book.</param>
/// <param name="BookTitle">Title of that book.</param>
/// <param name="SetNumber">1-based set, 0 for a book title hit.</param>
/// <param name="MacroIndex">0-based macro slot, -1 for a book title hit.</param>
/// <param name="LineIndex">0-based line, -1 for a name or title hit.</param>
/// <param name="Field">Which field matched.</param>
/// <param name="Text">The matching text.</param>
public sealed record MacroSearchHit(
    CharacterFolder Character,
    int BookNumber,
    string BookTitle,
    int SetNumber,
    int MacroIndex,
    int LineIndex,
    MacroSearchField Field,
    string Text)
{
    /// <summary>Readable location, e.g. <c>Kaelith · Book 15 « PldRunR » · Set 1 · Ctrl-2 · ligne 1</c>.</summary>
    public string Location
    {
        get
        {
            string place = $"{Character.Label} · Book {BookNumber} « {BookTitle} »";
            if (SetNumber > 0)
                place += $" · Set {SetNumber}";
            if (MacroIndex >= 0)
                place += $" · {MacroSlot.Describe(MacroIndex)}";
            if (LineIndex >= 0)
                place += $" · ligne {LineIndex + 1}";
            return place;
        }
    }

    public override string ToString() => $"{Location} : {Text}";
}

public sealed class MacroSearchOptions
{
    public bool MatchCase { get; init; }

    public bool SearchLines { get; init; } = true;

    public bool SearchNames { get; init; } = true;

    public bool SearchTitles { get; init; } = true;

    /// <summary>Restrict to one character; null searches them all.</summary>
    public CharacterFolder? OnlyCharacter { get; init; }

    /// <summary>Stop after this many hits, so a common word cannot flood the UI.</summary>
    public int MaxHits { get; init; } = 500;
}

/// <summary>Finds a phrase across every book of every character.</summary>
public static class MacroSearch
{
    public static IReadOnlyList<MacroSearchHit> Search(
        MacroLibrary library, string query, MacroSearchOptions? options = null, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        options ??= new MacroSearchOptions();

        var hits = new List<MacroSearchHit>();
        if (string.IsNullOrEmpty(query))
            return hits;

        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var characters = options.OnlyCharacter is null
            ? library.Characters
            : [options.OnlyCharacter];

        foreach (var character in characters)
        {
            foreach (var book in character.Books)
            {
                if (options.SearchTitles && !book.IsUntitled && book.Title.Contains(query, comparison))
                {
                    hits.Add(new MacroSearchHit(character, book.Number, book.Title, 0, -1, -1,
                        MacroSearchField.BookTitle, book.Title));
                    if (hits.Count >= options.MaxHits)
                        return hits;
                }

                if (!options.SearchLines && !options.SearchNames)
                    continue;

                foreach (var set in book.Sets)
                {
                    if (!set.Exists || !set.HasExpectedSize)
                        continue;

                    MacroBook loaded;
                    try
                    {
                        loaded = set.Load();
                    }
                    catch (MacroFileException ex)
                    {
                        log.Warn($"Search skipped {set.FullPath}: {ex.Message}");
                        continue;
                    }

                    for (int index = 0; index < MacroBook.MacroCount; index++)
                    {
                        var macro = loaded.Macros[index];

                        if (options.SearchNames && macro.Name.Contains(query, comparison))
                        {
                            hits.Add(new MacroSearchHit(character, book.Number, book.Title, set.SetNumber, index, -1,
                                MacroSearchField.MacroName, macro.Name));
                            if (hits.Count >= options.MaxHits)
                                return hits;
                        }

                        if (!options.SearchLines)
                            continue;

                        for (int line = 0; line < Macro.LineCount; line++)
                        {
                            if (macro.Lines[line].Length == 0 || !macro.Lines[line].Contains(query, comparison))
                                continue;

                            hits.Add(new MacroSearchHit(character, book.Number, book.Title, set.SetNumber, index, line,
                                MacroSearchField.MacroLine, macro.Lines[line]));
                            if (hits.Count >= options.MaxHits)
                                return hits;
                        }
                    }
                }
            }
        }

        log.Info($"Search for '{query}': {hits.Count} hit(s).");
        return hits;
    }
}
