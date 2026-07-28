namespace FfxiMacros.Core.Io;

/// <summary>
/// <c>mcr.sys</c>: the set a character is on, recorded by the game itself.
/// </summary>
/// <remarks>
/// <para>
/// 28 bytes — the usual 24-byte container over a single 32-bit value, and that value is a raw set
/// file index, 0..399: the same numbering as <c>mcr140.dat</c>. So 140 means book 15, set 1.
/// </para>
/// <para>
/// Read off four live installs: 140 for a character parked on book 15, 20 for one on book 3, 6 for
/// one on set 7 of book 1, and 0 for two that had never moved. It is written when the client saves
/// its state — at login, and when it leaves — not on every book change, so it says where the game
/// put the character rather than where the player is standing this second.
/// </para>
/// </remarks>
public static class MacroSystemFile
{
    public const string FileName = "mcr.sys";
    public const int DataSize = 4;
    public const int FileSize = FfxiContainer.HeaderSize + DataSize;   // 28

    /// <summary>
    /// The set index recorded for a character folder, or null when the file is absent, damaged or
    /// holds a value outside 0..399.
    /// </summary>
    /// <remarks>
    /// Never throws: this is a hint shown beside the books, and a client that writes something
    /// unexpected there must not stop the folder from opening.
    /// </remarks>
    public static int? ReadFileIndex(string folder)
    {
        string path = Path.Combine(folder, FileName);

        try
        {
            if (!File.Exists(LongPath.Normalize(path)))
                return null;

            byte[] raw = LongPath.ReadAllBytes(path);
            if (raw.Length != FileSize)
                return null;

            var (_, payload, _) = FfxiContainer.Read(raw, DataSize, "macro state");
            int index = BitConverter.ToInt32(payload);

            return index >= 0 && index < MacroFileNaming.FileCount ? index : null;
        }
        catch (Exception ex) when (ex is MacroFileException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>When the game last wrote the file, or null when there is none.</summary>
    public static DateTime? WrittenUtc(string folder)
    {
        try
        {
            var file = new FileInfo(LongPath.Normalize(Path.Combine(folder, FileName)));
            return file.Exists ? file.LastWriteTimeUtc : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
