using System.Text.RegularExpressions;
using FfxiMacros.Core.Diagnostics;

namespace FfxiMacros.Core.Text;

/// <summary>
/// Turns the four payload bytes of an auto-translate phrase into a readable name, and back.
/// </summary>
/// <remarks>
/// <para>
/// A phrase is stored as <c>FD c1 c2 idHi idLo FD</c>. Confirmed against 108 distinct phrases from
/// real macro files: <c>c2</c> is always <c>0x02</c>, <c>idHi idLo</c> is a big-endian 16-bit id, and
/// <c>c1</c> selects which of the game's tables the id belongs to:
/// </para>
/// <list type="bullet">
///   <item><description><c>0x02</c> — the auto-translate phrase list (Provoke, Savage Blade, Haste Samba…).
///   105 of the 108 phrases.</description></item>
///   <item><description><c>0x07</c> — the item list (Forbidden Key, Panacea, Foil). The other 3.</description></item>
/// </list>
/// <para>
/// The names themselves are game data, so they are read from an installed copy of Windower's
/// resource files rather than shipped here. With no dictionary loaded every phrase simply stays in
/// its <c>{AT:02021F01}</c> hex form — readable enough to be safe, and never lossy.
/// </para>
/// </remarks>
public sealed class AutoTranslateDictionary
{
    /// <summary>Payload byte selecting the auto-translate phrase table.</summary>
    public const byte PhraseCategory = 0x02;

    /// <summary>Payload byte selecting the item table.</summary>
    public const byte ItemCategory = 0x07;

    private const byte SubCategory = 0x02;

    private readonly Dictionary<uint, string> _nameByPayload = [];

    // One reverse map per table: the game names a "Foil" spell and a "Foil" scroll, and those must
    // not shadow each other. Within a table a repeated name is dropped, so a name that survives
    // here always points at exactly one phrase.
    private readonly Dictionary<byte, Dictionary<string, byte[]>> _payloadByName = [];
    private readonly Dictionary<byte, HashSet<string>> _ambiguousNames = [];

    /// <summary>Matches one <c>[id] = {id=…,en="…"</c> row of a Windower resource file.</summary>
    private static readonly Regex ResourceRow = new(
        @"\[(?<id>\d+)\]\s*=\s*\{\s*id\s*=\s*\d+\s*,\s*en\s*=\s*""(?<en>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A dictionary that resolves nothing; every phrase stays in hex form.</summary>
    public static AutoTranslateDictionary Empty { get; } = new();

    public int Count => _nameByPayload.Count;

    public bool IsEmpty => _nameByPayload.Count == 0;

    /// <summary>Where the names were read from, for the status line.</summary>
    public string? SourceDescription { get; private set; }

    /// <summary>Readable name for a four-byte payload.</summary>
    public bool TryGetName(ReadOnlySpan<byte> payload, out string name)
    {
        name = "";
        return payload.Length == 4 && _nameByPayload.TryGetValue(Key(payload), out name!);
    }

    /// <summary>
    /// Four-byte payload for a name within one table. A name the game reuses resolves to its first
    /// id; the callers that must preserve the others compare what comes back against the bytes they
    /// started from, and spell the id out when it differs.
    /// </summary>
    public bool TryGetPayload(string name, byte category, out byte[] payload)
    {
        payload = [];
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (_payloadByName.TryGetValue(category, out var table) && table.TryGetValue(name.Trim(), out byte[]? found))
        {
            payload = found;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A phrase the user can pick, with the text to type for it.
    /// </summary>
    /// <param name="Name">Readable name, e.g. <c>Mighty Strikes</c>.</param>
    /// <param name="IsItem">True when it comes from the item table rather than the phrase list.</param>
    /// <param name="Escape">What to write in a macro line, e.g. <c>{AT:Mighty Strikes}</c>.</param>
    public sealed record Phrase(string Name, bool IsItem, string Escape);

    /// <summary>
    /// Finds the phrases whose name matches what has been typed so far. Names that start with the
    /// query come first — typing "mighty" should offer "Mighty Strikes" before "Aegis of Mighty".
    /// </summary>
    /// <param name="query">What the user typed; blank returns nothing.</param>
    /// <param name="max">How many to return at most.</param>
    /// <param name="includeItems">Search the item table too; there are 23 000 of them.</param>
    public IReadOnlyList<Phrase> Search(string query, int max = 40, bool includeItems = true)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        query = query.Trim();
        var starts = new List<Phrase>();
        var contains = new List<Phrase>();

        foreach (var (category, table) in _payloadByName)
        {
            bool item = category == ItemCategory;
            if (item && !includeItems)
                continue;

            foreach (string name in table.Keys)
            {
                int at = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    continue;

                var phrase = new Phrase(name, item, item ? $"{{AT:item {name}}}" : $"{{AT:{name}}}");
                (at == 0 ? starts : contains).Add(phrase);

                if (starts.Count >= max)
                    break;
            }
        }

        return starts
            .OrderBy(m => m.IsItem)
            .ThenBy(m => m.Name.Length)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(contains
                .OrderBy(m => m.IsItem)
                .ThenBy(m => m.Name.Length)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            .Take(max)
            .ToList();
    }

    /// <summary>True when the name exists in that table but names more than one phrase.</summary>
    public bool IsAmbiguous(string name, byte category) =>
        _ambiguousNames.TryGetValue(category, out var names) && names.Contains(name.Trim());

    /// <summary>Builds the four payload bytes for a table and a phrase id.</summary>
    public static byte[] Payload(byte category, ushort id) =>
        [category, SubCategory, (byte)(id >> 8), (byte)id];

    /// <summary>
    /// Reads Windower's <c>auto_translates.lua</c> and <c>items.lua</c> from a <c>res</c> folder.
    /// A missing or unreadable file is logged and skipped, never fatal.
    /// </summary>
    public static AutoTranslateDictionary LoadFromWindower(string resourceFolder, IMacroLog? log = null)
    {
        var dictionary = new AutoTranslateDictionary();
        dictionary.AddWindower(resourceFolder, log);
        return dictionary;
    }

    /// <summary>
    /// Reads the names from the game's own data files, through <c>VTABLE.DAT</c> / <c>FTABLE.DAT</c>.
    /// </summary>
    public static AutoTranslateDictionary LoadFromGame(string installRoot, IMacroLog? log = null)
    {
        var dictionary = new AutoTranslateDictionary();
        dictionary.AddGame(installRoot, log);
        return dictionary;
    }

    /// <summary>
    /// Builds the dictionary from whatever is available: the game itself first — it needs nothing
    /// installed beyond FFXI — then Windower, which fills in the items and the phrases the game
    /// keeps as client markers. Returns <see cref="Empty"/> when neither is present.
    /// </summary>
    /// <param name="installRoot">The <c>FINAL FANTASY XI</c> folder, or null to skip the game.</param>
    /// <param name="windowerFolder">A Windower folder or its <c>res</c>, or null to search for one.</param>
    public static AutoTranslateDictionary AutoLoad(
        string? installRoot = null, string? windowerFolder = null, IMacroLog? log = null)
    {
        var dictionary = new AutoTranslateDictionary();
        var sources = new List<string>();

        if (!string.IsNullOrWhiteSpace(installRoot) && dictionary.AddGame(installRoot, log) is { } fromGame)
            sources.Add($"le jeu ({Path.GetFileName(fromGame)})");

        string? folder = LocateResources(windowerFolder, log);
        if (folder is not null && dictionary.AddWindower(folder, log))
            sources.Add($"Windower ({folder})");

        if (dictionary.IsEmpty)
        {
            log.Info("Auto-translate: no name source found; phrases stay in hex form.");
            return Empty;
        }

        dictionary.SourceDescription = string.Join(" + ", sources);
        log.Info($"Auto-translate: {dictionary.Count} phrase(s) from {dictionary.SourceDescription}.");
        return dictionary;
    }

    /// <summary>Adds the game's own names. Returns the file they came from, or null.</summary>
    private string? AddGame(string installRoot, IMacroLog? log)
    {
        var game = GameData.GameAutoTranslateLoader.TryLoad(installRoot, log);
        if (game is null)
            return null;

        foreach (var (id, name) in game.Phrases)
            Add(PhraseCategory, id, name);

        return game.Source;
    }

    /// <summary>Adds Windower's names for everything not already known.</summary>
    private bool AddWindower(string resourceFolder, IMacroLog? log)
    {
        int before = Count;
        LoadTable(Path.Combine(resourceFolder, "auto_translates.lua"), PhraseCategory, log);
        LoadTable(Path.Combine(resourceFolder, "items.lua"), ItemCategory, log);

        if (Count > before)
            SourceDescription ??= resourceFolder;

        return Count > before;
    }

    /// <summary>
    /// Finds a Windower <c>res</c> folder: the configured path first, then the usual install spots.
    /// Windower is installed wherever the player likes, so each fixed drive is checked one level deep.
    /// </summary>
    public static string? LocateResources(string? configuredFolder = null, IMacroLog? log = null)
    {
        foreach (string candidate in Candidates(configuredFolder, log))
        {
            try
            {
                if (File.Exists(Path.Combine(candidate, "auto_translates.lua")))
                {
                    log.Debug($"Windower resources: {candidate}");
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Debug($"Cannot inspect {candidate}: {ex.Message}");
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string? configuredFolder, IMacroLog? log)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder))
        {
            yield return configuredFolder;
            yield return Path.Combine(configuredFolder, "res");
        }

        var roots = new List<string>();
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string root = Environment.GetFolderPath(special, Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrEmpty(root))
                roots.Add(root);
        }

        try
        {
            roots.AddRange(DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Debug($"Cannot enumerate drives: {ex.Message}");
        }

        foreach (string root in roots)
        {
            IEnumerable<string> folders;
            try
            {
                // Folder names in the wild range from "Windower4" to "Windower Kaelith".
                folders = Directory.EnumerateDirectories(root, "Windower*").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Debug($"Cannot list {root}: {ex.Message}");
                continue;
            }

            foreach (string folder in folders)
                yield return Path.Combine(folder, "res");
        }
    }

    private void LoadTable(string path, byte category, IMacroLog? log)
    {
        string text;
        try
        {
            if (!File.Exists(path))
            {
                log.Debug($"Auto-translate: {path} not found.");
                return;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Auto-translate: cannot read {path} ({ex.Message}).");
            return;
        }

        int added = 0;
        foreach (Match match in ResourceRow.Matches(text))
        {
            if (int.TryParse(match.Groups["id"].Value, out int id) && id is >= 0 and <= ushort.MaxValue
                && Add(category, (ushort)id, Unescape(match.Groups["en"].Value)))
            {
                added++;
            }
        }

        log.Debug($"Auto-translate: {added} entries from {Path.GetFileName(path)}.");
    }

    /// <summary>
    /// Records one phrase. An id already known keeps the name it has, so the first source wins and
    /// later ones only fill the gaps.
    /// </summary>
    private bool Add(byte category, ushort id, string name)
    {
        // '}' would close the escape early, and '#' separates a name from its id.
        if (name.Length == 0 || name.Contains('}', StringComparison.Ordinal) || name.Contains('#', StringComparison.Ordinal))
            return false;

        byte[] payload = Payload(category, id);
        if (!_nameByPayload.TryAdd(Key(payload), name))
            return false;

        var byName = _payloadByName.TryGetValue(category, out var table)
            ? table
            : _payloadByName[category] = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = _ambiguousNames.TryGetValue(category, out var names)
            ? names
            : _ambiguousNames[category] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Some names cover several ids: "Animated Flourish" is 8094, 8095 and 8117, "Vallation" is
        // 8156 and 8178. The first id keeps the bare name — both sources list ids in order, so that
        // is the one the game's own menu inserts — and the later ones are written back as
        // "Animated Flourish#1F9F", which is the only way their bytes survive a save.
        if (byName.TryGetValue(name, out byte[]? previous))
        {
            if (!previous.AsSpan().SequenceEqual(payload))
                ambiguous.Add(name);
        }
        else
        {
            byName[name] = payload;
        }

        return true;
    }

    private static uint Key(ReadOnlySpan<byte> payload) =>
        ((uint)payload[0] << 24) | ((uint)payload[1] << 16) | ((uint)payload[2] << 8) | payload[3];

    private static string Unescape(string value) =>
        value.Contains('\\', StringComparison.Ordinal)
            ? value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)
            : value;
}
