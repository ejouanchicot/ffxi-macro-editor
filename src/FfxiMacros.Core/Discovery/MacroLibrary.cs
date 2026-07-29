using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Settings;

namespace FfxiMacros.Core.Discovery;

/// <summary>
/// Everything found under one <c>USER</c> folder: the characters, their books and their sets.
/// This is the root the UI tree binds to.
/// </summary>
public sealed class MacroLibrary
{
    private MacroLibrary(string userFolder, IReadOnlyList<CharacterFolder> characters)
    {
        UserFolder = userFolder;
        Characters = characters;
    }

    public string UserFolder { get; }

    /// <summary>
    /// Characters, in a stable order: by the name they go by.
    /// </summary>
    /// <remarks>
    /// They used to be listed most recently played first, which reads well for one player and badly
    /// for two: with both clients running and writing their books, the list reshuffled itself on
    /// every refresh. The list is now something to learn, and « who was played last » is only used
    /// to decide which book to open on.
    /// </remarks>
    public IReadOnlyList<CharacterFolder> Characters { get; }

    /// <summary>The character with the most recent macro write — usually the one being played.</summary>
    public CharacterFolder? MostRecent =>
        Characters.Count == 0 ? null : Characters.MaxBy(c => c.LastWriteUtc);

    public CharacterFolder? ById(string id) =>
        Characters.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Scans a <c>USER</c> folder. Readable names come from <paramref name="settings"/> when supplied.
    /// </summary>
    /// <exception cref="MacroFileException">The folder does not exist or cannot be listed.</exception>
    public static MacroLibrary Scan(string userFolder, EditorSettings? settings = null, IMacroLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userFolder);

        string? resolved = UserFolderLocator.Resolve(userFolder);
        if (resolved is null)
        {
            if (!Directory.Exists(userFolder))
                throw new MacroFileException("This folder does not exist.") { Path = userFolder };

            throw new MacroFileException(
                "This folder holds no character data. Pick the USER folder inside " +
                @"'SquareEnix\FINAL FANTASY XI'.")
            { Path = userFolder };
        }

        var characters = new List<CharacterFolder>();

        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(resolved).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MacroFileException($"Could not list the USER folder: {ex.Message}", ex) { Path = resolved };
        }

        foreach (string folder in folders)
        {
            if (!CharacterFolder.LooksLikeCharacterFolder(folder))
            {
                log.Debug($"Skipping '{Path.GetFileName(folder)}': no macro files inside.");
                continue;
            }

            try
            {
                var character = CharacterFolder.Scan(folder, log);
                character.DisplayName = settings?.NameFor(character.Id);
                if (!character.HasHexId)
                    log.Info($"'{character.Id}' is not a hexadecimal character id, but it holds macro files; keeping it.");
                characters.Add(character);
            }
            catch (MacroFileException ex)
            {
                log.Error($"Skipping character folder {folder}: {ex.Message}");
            }
        }

        // The character you actually play comes first, and stays first: the one with the most macro
        // files is the main, and that count does not change from one minute to the next. Sorting by
        // the most recent write put whichever client had just flushed at the top, so the list
        // reshuffled itself while playing; sorting by name buried the main under an alt.
        characters.Sort((a, b) =>
        {
            int bySize = b.SetFileCount.CompareTo(a.SetFileCount);
            return bySize != 0 ? bySize : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        });
        log.Info($"{resolved}: {characters.Count} character(s), {characters.Sum(c => c.SetFileCount)} macro set file(s).");

        return new MacroLibrary(resolved, characters);
    }

    /// <summary>
    /// Copies a character folder into a timestamped subfolder of <paramref name="backupRoot"/>.
    /// Only macro files are copied — never the whole folder, which holds megabytes of unrelated data.
    /// </summary>
    /// <returns>The backup folder that was created.</returns>
    public static string BackupCharacter(CharacterFolder character, string backupRoot, DateTime? stamp = null, IMacroLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);

        string target = Path.Combine(
            backupRoot,
            $"{character.Id}-{(stamp ?? DateTime.Now):yyyyMMdd-HHmmss}");

        try
        {
            Directory.CreateDirectory(target);

            int copied = 0;
            foreach (string pattern in new[] { MacroFileNaming.SearchPattern, "mcr*.ttl", "mcr.sys" })
            {
                foreach (string file in Directory.EnumerateFiles(character.Path, pattern))
                {
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                    copied++;
                }
            }

            log.Info($"Backed up {copied} file(s) from {character.Path} to {target}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MacroFileException($"Backup failed: {ex.Message}", ex) { Path = target };
        }

        return target;
    }
}
