using System.Runtime.InteropServices;

namespace FfxiMacros.Core.Io;

/// <summary>
/// Windows long-path handling. FFXI's <c>USER</c> folder sits deep inside Steam library paths, and the
/// original tool failed silently on "path too long"; here we opt into the extended-length form and,
/// failing that, report a real error.
/// </summary>
public static class LongPath
{
    private const int TraditionalMaxPath = 259;
    private const string ExtendedPrefix = @"\\?\";
    private const string ExtendedUncPrefix = @"\\?\UNC\";

    /// <summary>
    /// Returns a path safe to hand to the file APIs: on Windows, long absolute paths get the
    /// <c>\\?\</c> prefix. On other platforms the path is returned unchanged.
    /// </summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return path;
        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
            return path;
        if (path.Length <= TraditionalMaxPath)
            return path;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (PathTooLongException ex)
        {
            throw new MacroFileException(
                $"Path is too long for Windows ({path.Length} characters). Move the game folder closer to the " +
                "drive root, or enable long paths (LongPathsEnabled) in Windows.", ex)
            { Path = path };
        }

        return full.StartsWith(@"\\", StringComparison.Ordinal)
            ? ExtendedUncPrefix + full[2..]
            : ExtendedPrefix + full;
    }

    /// <summary>Strips the <c>\\?\</c> prefix so paths can be shown to the user.</summary>
    public static string ForDisplay(string path)
    {
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.Ordinal))
            return @"\\" + path[ExtendedUncPrefix.Length..];
        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
            return path[ExtendedPrefix.Length..];
        return path;
    }

    public static byte[] ReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(Normalize(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            throw new MacroFileException($"Could not read the file: {ex.Message}", ex) { Path = ForDisplay(path) };
        }
    }

    /// <summary>
    /// Writes via a temporary file in the same folder, then swaps it in, so an interrupted save
    /// can never leave a half-written macro file behind.
    /// </summary>
    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        string normalized = Normalize(path);
        string temp = normalized + ".tmp";

        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, normalized, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            TryDelete(temp);
            throw new MacroFileException($"Could not write the file: {ex.Message}", ex) { Path = ForDisplay(path) };
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort only: the real error is the one we are about to throw.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
