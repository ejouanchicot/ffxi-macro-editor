using FfxiMacros.Core.Diagnostics;

namespace FfxiMacros.Core.GameData;

/// <param name="Phrases">Phrase id to readable name, markers already resolved.</param>
/// <param name="Unresolved">Phrases left as a client marker this reader cannot expand.</param>
/// <param name="Source">Path the dictionary was read from.</param>
public sealed record GameAutoTranslate(
    IReadOnlyDictionary<ushort, string> Phrases,
    int Unresolved,
    string Source);

/// <summary>
/// Finds and reads the auto-translate names from an FFXI installation, using nothing but the game's
/// own files.
/// </summary>
public static class GameAutoTranslateLoader
{
    // Where these files sit on a current install. They are only a starting guess: each one is
    // validated by content, and a full sweep takes over if a game update moves them.
    private const int DictionaryIdHint = 55665;
    private const int AbilityTableIdHint = 55701;
    private const int SpellTableIdHint = 55702;

    /// <summary>
    /// Reads the dictionary, or returns null when this is not a usable installation.
    /// Never throws for missing or unexpected data — the caller falls back to the hex form.
    /// </summary>
    public static GameAutoTranslate? TryLoad(string installRoot, IMacroLog? log = null)
    {
        FfxiDatIndex index;
        try
        {
            index = FfxiDatIndex.Load(installRoot, log);
        }
        catch (Io.MacroFileException ex)
        {
            log.Warn($"Game data: {ex.Message}");
            return null;
        }

        var (dictionary, source) = FindDictionary(index, log);
        if (dictionary is null)
        {
            log.Info($"Game data: no auto-translate dictionary found under {installRoot}.");
            return null;
        }

        var (abilities, spells) = FindNameTables(index, dictionary, log);

        var phrases = new Dictionary<ushort, string>(dictionary.Phrases.Count);
        int unresolved = 0;

        foreach (var (id, text) in dictionary.Phrases)
        {
            string? name = AutoTranslateDat.Resolve(text, abilities, spells);
            if (name is { Length: > 0 })
                phrases[id] = name;
            else
                unresolved++;
        }

        log.Info($"Game data: {phrases.Count} auto-translate phrase(s) from {source}"
                 + (unresolved > 0 ? $", {unresolved} left as client markers." : "."));

        return new GameAutoTranslate(phrases, unresolved, source);
    }

    /// <summary>Tries the usual file id, then sweeps the install for the dictionary's signature.</summary>
    private static (AutoTranslateDat? Dat, string Source) FindDictionary(FfxiDatIndex index, IMacroLog? log)
    {
        if (index.PathOf(DictionaryIdHint) is { } hinted
            && AutoTranslateDat.TryLoad(hinted, log) is { } fromHint
            && LooksComplete(fromHint))
        {
            return (fromHint, hinted);
        }

        log.Debug("Game data: the usual dictionary id did not match; sweeping the install.");

        AutoTranslateDat? best = null;
        string bestPath = "";
        foreach (int id in index.UsedIds)
        {
            string? path = index.PathOf(id);
            if (path is null || !HasDictionarySignature(path))
                continue;

            var candidate = AutoTranslateDat.TryLoad(path, log);
            if (candidate is null || !LooksComplete(candidate))
                continue;

            if (best is null || candidate.Phrases.Count > best.Phrases.Count)
            {
                best = candidate;
                bestPath = path;
            }
        }

        return (best, bestPath);
    }

    /// <summary>
    /// Picks the tables the <c>@Y</c> and <c>@C</c> markers point into, by trying the usual ids and
    /// then, if needed, scoring every table in the install against the markers themselves.
    /// </summary>
    private static (DMsgTable? Abilities, DMsgTable? Spells) FindNameTables(
        FfxiDatIndex index, AutoTranslateDat dictionary, IMacroLog? log)
    {
        var wantedY = MarkerIndexes(dictionary, 'Y');
        var wantedC = MarkerIndexes(dictionary, 'C');

        var abilities = TryHint(index, AbilityTableIdHint, wantedY);
        var spells = TryHint(index, SpellTableIdHint, wantedC);

        if (abilities is not null && spells is not null)
            return (abilities, spells);

        log.Debug("Game data: name tables not where expected; sweeping for them.");

        int bestY = 0, bestC = 0;
        foreach (int id in index.UsedIds)
        {
            string? path = index.PathOf(id);
            if (path is null || !DMsgTable.IsTable(path))
                continue;

            var table = DMsgTable.TryLoad(path);
            if (table is null)
                continue;

            if (abilities is null && Covers(table, wantedY) is var scoreY && scoreY > bestY)
            {
                bestY = scoreY;
                if (scoreY == wantedY.Count && wantedY.Count > 0)
                    abilities = table;
            }

            if (spells is null && Covers(table, wantedC) is var scoreC && scoreC > bestC)
            {
                bestC = scoreC;
                if (scoreC == wantedC.Count && wantedC.Count > 0)
                    spells = table;
            }

            if (abilities is not null && spells is not null)
                break;
        }

        return (abilities, spells);
    }

    /// <summary>A table matches when every marker index it is meant to cover holds some text.</summary>
    private static DMsgTable? TryHint(FfxiDatIndex index, int id, IReadOnlyCollection<int> wanted)
    {
        if (wanted.Count == 0)
            return null;

        string? path = index.PathOf(id);
        if (path is null)
            return null;

        var table = DMsgTable.TryLoad(path);
        return table is not null && Covers(table, wanted) == wanted.Count ? table : null;
    }

    private static int Covers(DMsgTable table, IReadOnlyCollection<int> wanted) =>
        wanted.Count(index => table.TryGet(index, out _));

    private static IReadOnlyCollection<int> MarkerIndexes(AutoTranslateDat dictionary, char kind)
    {
        var indexes = new HashSet<int>();
        foreach (string text in dictionary.Phrases.Values)
        {
            if (text.Length > 2 && text[0] == '@' && text[1] == kind
                && int.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    /// <summary>Guards against a file that merely starts with the right bytes.</summary>
    private static bool LooksComplete(AutoTranslateDat dat) =>
        dat.Phrases.Count > 100 && dat.Groups.Count > 4;

    private static bool HasDictionarySignature(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            return stream.Read(head) == 4 && AutoTranslateDat.HasSignature(head);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
