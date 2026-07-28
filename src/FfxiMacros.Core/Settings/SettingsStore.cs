using System.Text.Json;
using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;

namespace FfxiMacros.Core.Settings;

/// <summary>Loads and saves <see cref="EditorSettings"/> as JSON under the user's roaming profile.</summary>
public static class SettingsStore
{
    public const string ApplicationFolderName = "FfxiMacroEditor";
    public const string FileName = "settings.json";

    /// <summary>Generated rather than reflected, so the settings survive a trimmed build.</summary>
    private static readonly SettingsJsonContext Context = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    });

    /// <summary><c>%APPDATA%\FfxiMacroEditor</c> on Windows, the XDG equivalent elsewhere.</summary>
    public static string ApplicationFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            ApplicationFolderName);

    public static string DefaultPath => Path.Combine(ApplicationFolder, FileName);

    public static string DefaultLogPath => Path.Combine(ApplicationFolder, "ffxi-macro-editor.log");

    public static string DefaultBackupFolder => Path.Combine(ApplicationFolder, "Backups");

    /// <summary>
    /// Reads the settings file, or returns defaults when it is missing or unreadable.
    /// A corrupt settings file is never fatal: it is reported and replaced on the next save.
    /// </summary>
    public static EditorSettings Load(string? path = null, IMacroLog? log = null)
    {
        path ??= DefaultPath;

        if (!File.Exists(path))
        {
            log.Debug($"No settings file at {path}; starting from defaults.");
            return new EditorSettings { SourcePath = path };
        }

        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(path), Context.EditorSettings)
                ?? new EditorSettings();
            settings.SourcePath = path;
            settings.CharacterNames = new Dictionary<string, string>(settings.CharacterNames, StringComparer.OrdinalIgnoreCase);
            log.Debug($"Settings loaded from {path}.");
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warn($"Could not read {path} ({ex.Message}); starting from defaults.");
            return new EditorSettings { SourcePath = path };
        }
    }

    /// <summary>Writes the settings file, creating the folder if needed.</summary>
    /// <exception cref="MacroFileException">The file could not be written.</exception>
    public static void Save(EditorSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        path ??= settings.SourcePath ?? DefaultPath;

        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MacroFileException($"Could not create the settings folder: {ex.Message}", ex) { Path = path };
        }

        LongPath.WriteAllBytesAtomic(path, JsonSerializer.SerializeToUtf8Bytes(settings, Context.EditorSettings));
        settings.SourcePath = path;
    }
}
