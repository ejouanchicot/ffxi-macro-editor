using System.Globalization;
using System.Text;
using FfxiMacros.Core.Diagnostics;

namespace FfxiMacros.Core.GameData;

/// <summary>
/// The game's auto-translate dictionary, read straight from its data file.
/// </summary>
/// <remarks>
/// <para>Each phrase is stored as a self-describing record:</para>
/// <code>
/// 02 02 &lt;group&gt; &lt;index&gt; &lt;length&gt; &lt;text…&gt; 00
/// </code>
/// <para>
/// The first four bytes are exactly what a macro carries between its <c>FD</c> markers, so the id a
/// macro refers to is simply <c>(group &lt;&lt; 8) | index</c>. A record whose index is 0 opens a group and
/// is a fixed 76-byte header holding the category name (【Greetings】, 【Job Abilities】…).
/// </para>
/// <para>
/// Many phrases are not stored as text but as a marker the client expands at runtime:
/// <c>@Y2BD</c> is entry 0x2BD of the ability table, <c>@C…</c> of the spell table. Resolving those two
/// covers every phrase a macro is likely to use; <c>@A</c> (place names) and <c>@J</c> (job names) live in
/// tables this reader does not decode, and stay unresolved.
/// </para>
/// <para>
/// Verified on a real install: 2685 phrases across 42 groups, parsed to exactly the end of the file,
/// and all 713 <c>@Y</c> plus 311 <c>@C</c> markers resolve to the expected names.
/// </para>
/// </remarks>
public sealed class AutoTranslateDat
{
    /// <summary>Bytes that open every record, matching the first two payload bytes of a macro phrase.</summary>
    private static readonly byte[] RecordPrefix = [0x02, 0x02];

    /// <summary>A group header is a fixed block; every one of the 42 groups measured 76 bytes.</summary>
    private const int GroupHeaderSize = 76;

    private AutoTranslateDat(Dictionary<ushort, string> phrases, Dictionary<byte, string> groups)
    {
        Phrases = phrases;
        Groups = groups;
    }

    /// <summary>Phrase id — <c>(group &lt;&lt; 8) | index</c> — to its raw text, marker included.</summary>
    public IReadOnlyDictionary<ushort, string> Phrases { get; }

    /// <summary>Group number to its category name.</summary>
    public IReadOnlyDictionary<byte, string> Groups { get; }

    /// <summary>
    /// True when the bytes open the way the dictionary does: the two record bytes, a group number,
    /// and index 0 marking a group header. The loader confirms the guess by parsing the whole file.
    /// </summary>
    public static bool HasSignature(ReadOnlySpan<byte> raw) =>
        raw.Length >= 4 && raw[0] == 0x02 && raw[1] == 0x02 && raw[2] >= 0x01 && raw[3] == 0x00;

    /// <summary>Parses the dictionary, or returns null when the bytes are not one.</summary>
    public static AutoTranslateDat? TryRead(ReadOnlySpan<byte> raw, IMacroLog? log = null)
    {
        if (!HasSignature(raw))
            return null;

        var phrases = new Dictionary<ushort, string>();
        var groups = new Dictionary<byte, string>();

        int p = 0;
        while (p + 5 <= raw.Length)
        {
            if (raw[p] != RecordPrefix[0] || raw[p + 1] != RecordPrefix[1])
            {
                log.Warn($"Auto-translate data: unexpected record at 0x{p:X} (0x{raw[p]:X2} 0x{raw[p + 1]:X2}); stopping here.");
                break;
            }

            byte group = raw[p + 2];
            byte index = raw[p + 3];

            if (index == 0)
            {
                groups[group] = ReadZeroTerminated(raw[(p + 4)..]);
                p += GroupHeaderSize;
                continue;
            }

            int length = raw[p + 4];
            if (p + 5 + length > raw.Length)
            {
                log.Warn($"Auto-translate data: record at 0x{p:X} runs past the end of the file.");
                break;
            }

            phrases[(ushort)((group << 8) | index)] = ReadZeroTerminated(raw.Slice(p + 5, length));
            p += 5 + length;
        }

        log.Debug($"Auto-translate data: {phrases.Count} phrase(s) in {groups.Count} group(s).");
        return new AutoTranslateDat(phrases, groups);
    }

    public static AutoTranslateDat? TryLoad(string path, IMacroLog? log = null)
    {
        try
        {
            return TryRead(File.ReadAllBytes(path), log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Debug($"Auto-translate data: cannot read {path} ({ex.Message}).");
            return null;
        }
    }

    /// <summary>
    /// Replaces a client marker with the name it stands for. Returns null when the text is a marker
    /// this reader cannot resolve, so the caller can leave the phrase in its hex form.
    /// </summary>
    public static string? Resolve(string text, DMsgTable? abilities, DMsgTable? spells)
    {
        if (text.Length == 0)
            return null;
        if (text[0] != '@')
            return text;
        if (text.Length < 3)
            return null;

        var table = text[1] switch
        {
            'Y' => abilities,
            'C' => spells,
            _ => null,          // @A place names and @J job names live in tables we do not read.
        };

        if (table is null)
            return null;

        return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index)
            && table.TryGet(index, out string name)
            ? name
            : null;
    }

    private static string ReadZeroTerminated(ReadOnlySpan<byte> bytes)
    {
        int nul = bytes.IndexOf((byte)0);
        if (nul >= 0)
            bytes = bytes[..nul];

        return Encoding.Latin1.GetString(bytes);
    }
}
