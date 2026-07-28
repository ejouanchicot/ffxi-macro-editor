using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;

namespace FfxiMacros.Core.Discovery;

/// <summary>How a candidate <c>USER</c> folder was found.</summary>
public enum UserFolderSource
{
    Configured,
    PlayOnline,
    Steam,
    DriveScan,
    Manual,
}

/// <param name="Path">Full path of the <c>USER</c> folder.</param>
/// <param name="Source">Where the candidate came from.</param>
/// <param name="CharacterCount">Character folders found inside.</param>
/// <param name="LastWriteUtc">Most recent macro file write, for picking the live install.</param>
public sealed record UserFolderCandidate(
    string Path,
    UserFolderSource Source,
    int CharacterCount,
    DateTime LastWriteUtc)
{
    public override string ToString() => $"{Path}  ({CharacterCount} character(s), {Source})";
}

/// <summary>
/// Finds the FFXI <c>USER</c> folder without hard-coding a single install path — the bug that made
/// the 2014 editor unusable on Steam and on non-default drives.
/// </summary>
public static class UserFolderLocator
{
    /// <summary>Overrides detection entirely, mostly for tests and support cases.</summary>
    public const string EnvironmentVariable = "FFXI_USER_DIR";

    private const string SquareEnixSuffix = @"SquareEnix\FINAL FANTASY XI\USER";
    private const string UserFolderName = "USER";

    private static readonly string[] PlayOnlineRoots =
    [
        @"PlayOnline\SquareEnix\FINAL FANTASY XI\USER",
        @"PlayOnlineViewer\SquareEnix\FINAL FANTASY XI\USER",
    ];

    private static readonly string[] SteamRootHints =
    [
        @"Steam",
        @"SteamLibrary",
        @"Games\Steam",
    ];

    /// <summary>
    /// True when <paramref name="path"/> looks like a <c>USER</c> folder: at least one subfolder
    /// holding macro files.
    /// </summary>
    public static bool IsUserFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            return Directory.EnumerateDirectories(path).Any(CharacterFolder.LooksLikeCharacterFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Accepts what a user is likely to pick in a folder browser — the <c>USER</c> folder itself, the
    /// game folder above it, or even a character folder — and returns the real <c>USER</c> folder.
    /// </summary>
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.Length == 0 || !Directory.Exists(path))
            return null;

        if (IsUserFolder(path))
            return path;

        // They pointed at "FINAL FANTASY XI" (or anything with a USER subfolder).
        string nested = Path.Combine(path, UserFolderName);
        if (IsUserFolder(nested))
            return nested;

        // They pointed at a single character folder.
        string? parent = Path.GetDirectoryName(path);
        if (CharacterFolder.LooksLikeCharacterFolder(path) && IsUserFolder(parent))
            return parent;

        return null;
    }

    /// <summary>
    /// Probes every known install layout and returns the candidates, richest and most recently used
    /// first. Nothing is ever thrown: an unreadable drive is logged and skipped.
    /// </summary>
    public static IReadOnlyList<UserFolderCandidate> Detect(string? configured = null, IMacroLog? log = null)
    {
        var found = new Dictionary<string, UserFolderCandidate>(StringComparer.OrdinalIgnoreCase);

        void Consider(string? path, UserFolderSource source)
        {
            string? resolved = Resolve(path);
            if (resolved is null)
                return;

            string key = Path.GetFullPath(resolved);
            if (found.ContainsKey(key))
                return;

            var (characters, lastWrite) = Summarize(key);
            found[key] = new UserFolderCandidate(key, source, characters, lastWrite);
            log.Info($"Found USER folder ({source}): {key} — {characters} character(s).");
        }

        Consider(Environment.GetEnvironmentVariable(EnvironmentVariable), UserFolderSource.Configured);
        Consider(configured, UserFolderSource.Configured);

        foreach (string path in PlayOnlineCandidates(log))
            Consider(path, UserFolderSource.PlayOnline);

        foreach (string path in SteamCandidates(log))
            Consider(path, UserFolderSource.Steam);

        foreach (string path in DriveScanCandidates(log))
            Consider(path, UserFolderSource.DriveScan);

        if (found.Count == 0)
            log.Warn("No FFXI USER folder was detected; the user must pick one manually.");

        return found.Values
            .OrderByDescending(c => c.Source == UserFolderSource.Configured)
            .ThenByDescending(c => c.CharacterCount)
            .ThenByDescending(c => c.LastWriteUtc)
            .ToList();
    }

    /// <summary>The single best candidate, or null when nothing was found.</summary>
    public static UserFolderCandidate? DetectBest(string? configured = null, IMacroLog? log = null) =>
        Detect(configured, log).FirstOrDefault();

    private static IEnumerable<string> PlayOnlineCandidates(IMacroLog? log)
    {
        var roots = new List<string>();

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.ProgramFiles,
                 })
        {
            string root = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrEmpty(root))
                roots.Add(root);
        }

        // PlayOnline is regularly installed straight onto a secondary drive.
        roots.AddRange(FixedDriveRoots(log));

        foreach (string root in roots)
        {
            foreach (string suffix in PlayOnlineRoots)
                yield return Path.Combine(root, suffix);
        }
    }

    /// <summary>
    /// Locates Steam libraries through <c>steamapps\libraryfolders.vdf</c>, then looks for the FFXI
    /// (FFXIPAL) install inside each of them.
    /// </summary>
    private static IEnumerable<string> SteamCandidates(IMacroLog? log)
    {
        foreach (string library in SteamLibraries(log))
        {
            string common = Path.Combine(library, "steamapps", "common");
            if (!Directory.Exists(common))
                continue;

            // FFXIPAL is the usual folder name, but the wrapper has been renamed over the years:
            // check every game folder for the SquareEnix\FINAL FANTASY XI\USER layout.
            IEnumerable<string> games;
            try
            {
                games = Directory.EnumerateDirectories(common).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Debug($"Cannot list {common}: {ex.Message}");
                continue;
            }

            foreach (string game in games)
                yield return Path.Combine(game, SquareEnixSuffix);
        }
    }

    /// <summary>Every Steam library root, including the Steam install itself.</summary>
    public static IReadOnlyList<string> SteamLibraries(IMacroLog? log = null)
    {
        var libraries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string steamRoot in SteamRoots(log))
        {
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;

            if (seen.Add(steamRoot))
                libraries.Add(steamRoot);

            var root = ValveKeyValues.ParseFile(vdf)["libraryfolders"];
            foreach (var entry in root.Children.Values)
            {
                // Modern Steam nests "path" in a block; ancient files map the index straight to a path.
                string? path = entry["path"].Value ?? entry.Value;
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && seen.Add(path))
                {
                    libraries.Add(path);
                    log.Debug($"Steam library: {path}");
                }
            }
        }

        return libraries;
    }

    private static IEnumerable<string> SteamRoots(IMacroLog? log)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string root = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrEmpty(root))
                yield return Path.Combine(root, "Steam");
        }

        foreach (string drive in FixedDriveRoots(log))
        {
            foreach (string hint in SteamRootHints)
                yield return Path.Combine(drive, hint);
        }
    }

    /// <summary>Shallow sweep of every fixed drive for an install in a non-standard place.</summary>
    private static IEnumerable<string> DriveScanCandidates(IMacroLog? log)
    {
        foreach (string drive in FixedDriveRoots(log))
        {
            yield return Path.Combine(drive, SquareEnixSuffix);
            yield return Path.Combine(drive, "Games", SquareEnixSuffix);
            yield return Path.Combine(drive, "FFXIPAL", SquareEnixSuffix);
        }
    }

    private static IReadOnlyList<string> FixedDriveRoots(IMacroLog? log)
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Debug($"Cannot enumerate drives: {ex.Message}");
            return [];
        }
    }

    private static (int Characters, DateTime LastWriteUtc) Summarize(string userFolder)
    {
        int characters = 0;
        DateTime lastWrite = DateTime.MinValue;

        try
        {
            foreach (string folder in Directory.EnumerateDirectories(userFolder))
            {
                if (!CharacterFolder.LooksLikeCharacterFolder(folder))
                    continue;

                characters++;
                DateTime write = Directory.GetLastWriteTimeUtc(folder);
                if (write > lastWrite)
                    lastWrite = write;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reported by the caller as a zero-character candidate.
        }

        return (characters, lastWrite);
    }
}
