using System.Globalization;

using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Settings;

namespace FfxiMacros.Core.Discovery;

/// <summary>Where a character is right now, as reported by the Windower addon.</summary>
public sealed record LiveMacroState(string CharacterId, string CharacterName, int Book, int Set, DateTime WrittenUtc)
{
    /// <summary>True when the book and set are inside what the game can actually hold.</summary>
    public bool IsUsable =>
        Book >= 1 && Book <= MacroFileNaming.BooksPerCharacter
        && Set >= 1 && Set <= MacroFileNaming.SetsPerBook;
}

/// <summary>
/// Reads what the <c>macrostate</c> Windower addon writes.
/// </summary>
/// <remarks>
/// <para>
/// The book a client has open exists only in its memory: nothing on disk names it, and Windower
/// offers no way to read it. What Windower can see is the <c>/macro book</c> and <c>/macro set</c>
/// commands going past — the ones the player types and the ones GearSwap sends on a job change. The
/// addon writes those down; this reads them back.
/// </para>
/// <para>
/// One small text file per character, <c>key=value</c> a line, in a folder this application owns.
/// Nothing here is trusted: a missing folder, a half-written file or a nonsensical book number all
/// mean "no report", never an error — the editor works perfectly well without any of it.
/// </para>
/// </remarks>
public static class LiveMacroStateStore
{
    public const string FolderName = "live";

    /// <summary><c>%APPDATA%\FfxiMacroEditor\live</c>.</summary>
    public static string DefaultFolder => Path.Combine(SettingsStore.ApplicationFolder, FolderName);

    /// <summary>Creates the folder, so the addon has somewhere to write before it is ever read.</summary>
    public static void EnsureFolder(string? folder = null, IMacroLog? log = null)
    {
        try
        {
            Directory.CreateDirectory(folder ?? DefaultFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Could not create the live state folder: {ex.Message}");
        }
    }

    public static IReadOnlyList<LiveMacroState> ReadAll(string? folder = null, IMacroLog? log = null)
    {
        folder ??= DefaultFolder;

        try
        {
            if (!Directory.Exists(folder))
                return [];

            return Directory.EnumerateFiles(folder, "*.txt")
                .Select(path => Read(path, log))
                .OfType<LiveMacroState>()
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Could not read the live state folder: {ex.Message}");
            return [];
        }
    }

    private static LiveMacroState? Read(string path, IMacroLog? log)
    {
        try
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int equals = line.IndexOf('=');
                if (equals > 0)
                    fields[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }

            var state = new LiveMacroState(
                fields.GetValueOrDefault("id", ""),
                fields.GetValueOrDefault("name", Path.GetFileNameWithoutExtension(path)),
                Number(fields, "book"),
                Number(fields, "set"),
                new FileInfo(path).LastWriteTimeUtc);

            return state.IsUsable ? state : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            log.Debug($"Ignoring live state file {path}: {ex.Message}");
            return null;
        }
    }

    private static int Number(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? text)
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
}
