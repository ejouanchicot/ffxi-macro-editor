using System.Text;

namespace FfxiMacros.Core.GameData;

/// <summary>
/// A <c>d_msg</c> string table — the game's format for lists of names (abilities, spells, …).
/// </summary>
/// <remarks>
/// <code>
/// 0x00  8   "d_msg\0\0\0"
/// 0x14  4   file size
/// 0x18  4   header size (64)
/// 0x20  4   entry size, when every entry has the same length
/// 0x24  4   data size
/// 0x28  4   entry count
/// </code>
/// Each fixed-size entry opens with a small descriptor and carries its NUL-terminated text at
/// offset 40. Confirmed against the ability table (5888 entries) and the spell table (1024).
/// </remarks>
public sealed class DMsgTable
{
    private const int MagicLength = 5;
    private const int TextOffset = 40;

    private static readonly byte[] Magic = "d_msg"u8.ToArray();

    private readonly string[] _entries;

    private DMsgTable(string[] entries) => _entries = entries;

    public int Count => _entries.Length;

    /// <summary>Entry text, or an empty string when the index is out of range.</summary>
    public string this[int index] =>
        index >= 0 && index < _entries.Length ? _entries[index] : "";

    public bool TryGet(int index, out string text)
    {
        text = this[index];
        return text.Length > 0;
    }

    /// <summary>True when the bytes start with the <c>d_msg</c> magic.</summary>
    public static bool HasMagic(ReadOnlySpan<byte> raw) =>
        raw.Length >= MagicLength && raw[..MagicLength].SequenceEqual(Magic);

    /// <summary>Reads a table, or returns null when the bytes are not a usable fixed-size d_msg.</summary>
    public static DMsgTable? TryRead(ReadOnlySpan<byte> raw)
    {
        if (!HasMagic(raw) || raw.Length < 0x2C)
            return null;

        int headerSize = BitConverter.ToInt32(raw.Slice(0x18, 4));
        int entrySize = BitConverter.ToInt32(raw.Slice(0x20, 4));
        int count = BitConverter.ToInt32(raw.Slice(0x28, 4));

        // Variable-length tables exist too; only the fixed-size layout is read here.
        if (headerSize <= 0 || entrySize <= TextOffset || count <= 0)
            return null;

        long end = (long)headerSize + ((long)count * entrySize);
        if (end > raw.Length)
            return null;

        var entries = new string[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = raw.Slice(headerSize + (i * entrySize), entrySize);
            ReadOnlySpan<byte> text = entry[TextOffset..];

            int nul = text.IndexOf((byte)0);
            if (nul >= 0)
                text = text[..nul];

            entries[i] = Encoding.Latin1.GetString(text);
        }

        return new DMsgTable(entries);
    }

    public static DMsgTable? TryLoad(string path)
    {
        try
        {
            return TryRead(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Cheap test used when sweeping the whole install: reads only the magic.</summary>
    public static bool IsTable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[MagicLength];
            return stream.Read(head) == MagicLength && HasMagic(head);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
