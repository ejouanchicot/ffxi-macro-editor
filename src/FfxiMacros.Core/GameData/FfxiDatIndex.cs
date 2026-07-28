using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;

namespace FfxiMacros.Core.GameData;

/// <summary>
/// The game's own file index: <c>VTABLE.DAT</c> and <c>FTABLE.DAT</c> together turn a data file id
/// into a path under <c>ROM</c>, <c>ROM2</c>, …
/// </summary>
/// <remarks>
/// <para>
/// <c>VTABLE.DAT</c> holds one byte per id — the ROM volume, or 0 when the id is unused.
/// <c>FTABLE.DAT</c> holds a little-endian 16-bit word per id, packing the directory in the upper
/// nine bits and the file number in the lower seven: <c>ROM&lt;vol&gt;/&lt;packed &gt;&gt; 7&gt;/&lt;packed &amp; 0x7F&gt;.DAT</c>.
/// That is why FTABLE is exactly twice the size of VTABLE, the one constraint the original spec knew.
/// </para>
/// <para>
/// Verified on a real install: 109 701 ids, of which 83 116 are in use, and every single one of them
/// resolves to a file that exists on disk.
/// </para>
/// </remarks>
public sealed class FfxiDatIndex
{
    public const string VTableFileName = "VTABLE.DAT";
    public const string FTableFileName = "FTABLE.DAT";

    private readonly byte[] _volumes;
    private readonly byte[] _files;

    private FfxiDatIndex(string root, byte[] volumes, byte[] files)
    {
        InstallRoot = root;
        _volumes = volumes;
        _files = files;
    }

    /// <summary>The <c>FINAL FANTASY XI</c> folder holding the tables and the ROM volumes.</summary>
    public string InstallRoot { get; }

    /// <summary>Number of ids the tables cover, used or not.</summary>
    public int Count => _volumes.Length;

    /// <summary>True when the folder holds both index tables.</summary>
    public static bool LooksLikeInstall(string? root) =>
        !string.IsNullOrWhiteSpace(root)
        && File.Exists(Path.Combine(root, VTableFileName))
        && File.Exists(Path.Combine(root, FTableFileName));

    /// <summary>
    /// The install folder for a <c>USER</c> folder: its parent, when that parent holds the tables.
    /// </summary>
    public static string? InstallRootFor(string? userFolder)
    {
        if (string.IsNullOrWhiteSpace(userFolder))
            return null;

        string? parent = Path.GetDirectoryName(userFolder.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return LooksLikeInstall(parent) ? parent : null;
    }

    /// <exception cref="MacroFileException">The tables are missing or inconsistent.</exception>
    public static FfxiDatIndex Load(string installRoot, IMacroLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        byte[] volumes = LongPath.ReadAllBytes(Path.Combine(installRoot, VTableFileName));
        byte[] files = LongPath.ReadAllBytes(Path.Combine(installRoot, FTableFileName));

        if (files.Length != volumes.Length * 2)
            throw new MacroFileException(
                $"{FTableFileName} is {files.Length} bytes; it must be exactly twice {VTableFileName} " +
                $"({volumes.Length} bytes).")
            { Path = installRoot };

        log.Debug($"Game index: {volumes.Length} file ids from {installRoot}.");
        return new FfxiDatIndex(installRoot, volumes, files);
    }

    /// <summary>Path of a data file, or null when the id is unused or the file is absent.</summary>
    public string? PathOf(int fileId)
    {
        if (fileId < 0 || fileId >= _volumes.Length)
            return null;

        byte volume = _volumes[fileId];
        if (volume == 0)
            return null;

        int packed = _files[fileId * 2] | (_files[(fileId * 2) + 1] << 8);
        string path = Path.Combine(
            InstallRoot,
            volume == 1 ? "ROM" : $"ROM{volume}",
            (packed >> 7).ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{packed & 0x7F}.DAT");

        return File.Exists(path) ? path : null;
    }

    /// <summary>Every id in use, in order.</summary>
    public IEnumerable<int> UsedIds
    {
        get
        {
            for (int id = 0; id < _volumes.Length; id++)
            {
                if (_volumes[id] != 0)
                    yield return id;
            }
        }
    }
}
