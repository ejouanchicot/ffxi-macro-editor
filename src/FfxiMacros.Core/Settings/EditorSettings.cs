using System.Text.Json.Serialization;

namespace FfxiMacros.Core.Settings;

/// <summary>
/// User preferences, persisted as JSON. Nothing here is ever hard-coded in the app —
/// the old tool's fatal flaw was a hard-wired install path.
/// </summary>
public sealed class EditorSettings
{
    /// <summary>The FFXI <c>USER</c> folder to work in. Null until detected or chosen.</summary>
    public string? UserFolder { get; set; }

    /// <summary>Previously used <c>USER</c> folders, most recent first.</summary>
    public List<string> RecentUserFolders { get; set; } = [];

    /// <summary>Readable name for each hexadecimal character folder, e.g. <c>a1b2c3d</c> -&gt; <c>Kaelith</c>.</summary>
    public Dictionary<string, string> CharacterNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Copy a character folder into <see cref="BackupFolder"/> before the first write of a session.</summary>
    public bool BackupBeforeSave { get; set; } = true;

    /// <summary>Where backups go. Null means a <c>Backups</c> folder next to the settings file.</summary>
    public string? BackupFolder { get; set; }

    /// <summary>Write a log file on every run, without needing <c>--debug</c>.</summary>
    public bool AlwaysLog { get; set; }

    /// <summary>
    /// Windower folder (or its <c>res</c> subfolder) used to name auto-translate phrases.
    /// Null means "look for one"; phrases stay in hex form when none is found.
    /// </summary>
    public string? WindowerFolder { get; set; }

    /// <summary>Interface language, <c>en</c> or <c>fr</c>. English until the user says otherwise.</summary>
    public string Language { get; set; } = "en";


    [JsonIgnore]
    public string? SourcePath { get; set; }

    public string? NameFor(string characterId) =>
        CharacterNames.TryGetValue(characterId, out string? name) && !string.IsNullOrWhiteSpace(name) ? name : null;

    public void SetName(string characterId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            CharacterNames.Remove(characterId);
        else
            CharacterNames[characterId] = name.Trim();
    }

    /// <summary>Records a folder as the current one and pushes it to the top of the recent list.</summary>
    public void UseUserFolder(string path, int maxRecent = 8)
    {
        UserFolder = path;
        RecentUserFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentUserFolders.Insert(0, path);
        if (RecentUserFolders.Count > maxRecent)
            RecentUserFolders.RemoveRange(maxRecent, RecentUserFolders.Count - maxRecent);
    }
}
