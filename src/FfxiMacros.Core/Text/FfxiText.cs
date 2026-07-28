using System.Globalization;
using System.Text;

namespace FfxiMacros.Core.Text;

/// <summary>
/// Lossless, human-editable text form for the fixed-size FFXI text fields found in <c>mcr*.dat</c>
/// and <c>mcr*.ttl</c>.
/// </summary>
/// <remarks>
/// <para>
/// Version 1 of the codec is an ASCII passthrough (0x20-0x7E), which covers every ordinary command
/// (<c>/ma "Cure IV" &lt;t&gt;</c>). Anything it cannot show as plain ASCII is escaped in braces so that
/// decode -&gt; encode always reproduces the original bytes byte for byte:
/// </para>
/// <list type="bullet">
///   <item><description><c>{AT:xxxxxxxx}</c> — an auto-translate phrase, stored in game as
///   <c>FD b1 b2 b3 b4 FD</c>; the eight hex digits are the four payload bytes.</description></item>
///   <item><description><c>{NN}</c> — any other byte, as two hex digits. Real files do contain these:
///   several lines written by the old 2014 editor start with a stray <c>{00}</c> where the leading
///   <c>/</c> should be. Surfacing them beats silently truncating the line at the NUL.</description></item>
///   <item><description><c>{{</c> — a literal <c>{</c>.</description></item>
/// </list>
/// <para>
/// Trailing NUL padding is stripped on decode and re-added on encode. Interior NULs are data.
/// Version 2 will resolve <c>{AT:...}</c> to readable phrase names using VTABLE.DAT/FTABLE.DAT,
/// on top of this same escaping scheme.
/// </para>
/// </remarks>
public static class FfxiText
{
    /// <summary>Byte that opens and closes an auto-translate phrase.</summary>
    public const byte AutoTranslateMarker = 0xFD;

    /// <summary>Total size of an auto-translate phrase on disk: <c>FD</c> + 4 payload bytes + <c>FD</c>.</summary>
    public const int AutoTranslateSize = 6;

    private const char EscapeOpen = '{';
    private const char EscapeClose = '}';

    /// <summary>Opens an auto-translate phrase, echoing the brackets the game draws around one.</summary>
    public const char PhraseOpen = '«';

    /// <summary>Closes an auto-translate phrase.</summary>
    public const char PhraseClose = '»';

    private const string AutoTranslatePrefix = "AT:";
    private const string ItemPrefix = "item ";
    private const char IdSeparator = '#';
    private const int AutoTranslatePayloadSize = 4;

    /// <summary>Second payload byte; <c>0x02</c> in every phrase seen in real files.</summary>
    private const byte AutoTranslateSubCategory = 0x02;

    /// <summary>
    /// Names used for auto-translate phrases when none is passed explicitly. Set once at startup;
    /// with the default empty dictionary every phrase stays in <c>{AT:02021F01}</c> hex form.
    /// </summary>
    public static AutoTranslateDictionary DefaultAutoTranslate { get; set; } = AutoTranslateDictionary.Empty;

    /// <summary>
    /// Usable text bytes in a field of <paramref name="fieldSize"/> bytes. The final byte is always
    /// NUL in every observed file (the original tool copies at most 60 bytes into a 61-byte line),
    /// so one byte is reserved as a terminator.
    /// </summary>
    public static int MaxTextBytes(int fieldSize) => fieldSize - 1;

    /// <summary>Decodes a fixed-size field into editable text. Trailing NUL padding is removed.</summary>
    /// <param name="field">The raw bytes of the field.</param>
    /// <param name="autoTranslate">
    /// Names for auto-translate phrases; defaults to <see cref="DefaultAutoTranslate"/>. A phrase is
    /// only written as a name when encoding that name reproduces the exact same bytes, so decoding
    /// stays lossless whatever dictionary is in use.
    /// </param>
    public static string Decode(ReadOnlySpan<byte> field, AutoTranslateDictionary? autoTranslate = null)
    {
        autoTranslate ??= DefaultAutoTranslate;

        int end = field.Length;
        while (end > 0 && field[end - 1] == 0)
            end--;
        if (end == 0)
            return "";

        var sb = new StringBuilder(end);
        for (int i = 0; i < end; i++)
        {
            byte b = field[i];

            if (b == AutoTranslateMarker && i + AutoTranslateSize - 1 < end
                && field[i + AutoTranslateSize - 1] == AutoTranslateMarker)
            {
                ReadOnlySpan<byte> payload = field.Slice(i + 1, AutoTranslatePayloadSize);
                sb.Append(PhraseOpen).Append(Describe(payload, autoTranslate)).Append(PhraseClose);
                i += AutoTranslateSize - 1;
                continue;
            }

            if (b == EscapeOpen)
            {
                sb.Append(EscapeOpen).Append(EscapeOpen);
                continue;
            }

            if (b is >= 0x20 and <= 0x7E)
            {
                sb.Append((char)b);
                continue;
            }

            sb.Append(EscapeOpen).Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append(EscapeClose);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders an auto-translate payload as a name when that is provably reversible, and as eight
    /// hex digits otherwise. Round-tripping the name back through <see cref="Encode"/> must land on
    /// the identical bytes, or the hex form wins — that check is what keeps saving byte-exact even
    /// when the game reuses a phrase name.
    /// </summary>
    private static string Describe(ReadOnlySpan<byte> payload, AutoTranslateDictionary autoTranslate)
    {
        byte category = payload[0];
        bool namedTable = category is AutoTranslateDictionary.PhraseCategory or AutoTranslateDictionary.ItemCategory;

        if (namedTable && autoTranslate.TryGetName(payload, out string name) && !LooksLikeHexPayload(name))
        {
            string prefix = category == AutoTranslateDictionary.ItemCategory ? ItemPrefix : "";

            // The bare name is enough when it points at exactly one phrase; otherwise the id is
            // spelled out so that saving writes back the very same bytes.
            if (autoTranslate.TryGetPayload(name, category, out byte[] roundTrip)
                && roundTrip.AsSpan().SequenceEqual(payload))
            {
                return prefix + name;
            }

            if (payload[1] == AutoTranslateSubCategory)
                return $"{prefix}{name}{IdSeparator}{payload[2]:X2}{payload[3]:X2}";
        }

        Span<char> hex = stackalloc char[AutoTranslatePayloadSize * 2];
        for (int k = 0; k < AutoTranslatePayloadSize; k++)
            payload[k].TryFormat(hex[(k * 2)..], out _, "X2", CultureInfo.InvariantCulture);

        return new string(hex);
    }

    /// <summary>Encodes editable text into a NUL-padded field of exactly <paramref name="fieldSize"/> bytes.</summary>
    /// <param name="text">Text in the escaped form produced by <see cref="Decode"/>.</param>
    /// <param name="fieldSize">Size of the on-disk field (61 for a line, 9 for a name, 16 for a title).</param>
    /// <param name="truncate">
    /// When true, text longer than the field is cut at a token boundary instead of throwing —
    /// an auto-translate phrase is never split in half.
    /// </param>
    /// <exception cref="FfxiTextException">The text is malformed, or too long and <paramref name="truncate"/> is false.</exception>
    public static byte[] Encode(string text, int fieldSize, bool truncate = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (fieldSize < 2)
            throw new ArgumentOutOfRangeException(nameof(fieldSize), fieldSize, "Field size must be at least 2 bytes.");

        int max = MaxTextBytes(fieldSize);
        var field = new byte[fieldSize];
        int written = 0;

        foreach (var token in Tokenize(text))
        {
            if (written + token.Length > max)
            {
                if (!truncate)
                    throw new FfxiTextException(
                        $"Text is longer than the {max} bytes available in this {fieldSize}-byte field.");
                break;
            }

            token.CopyTo(field.AsSpan(written));
            written += token.Length;
        }

        return field;
    }

    /// <summary>Non-throwing variant of <see cref="Encode(string,int,bool)"/>, for live UI validation.</summary>
    public static bool TryEncode(string text, int fieldSize, out byte[] field, out string? error)
    {
        try
        {
            field = Encode(text, fieldSize);
            error = null;
            return true;
        }
        catch (FfxiTextException ex)
        {
            field = new byte[fieldSize];
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Number of bytes <paramref name="text"/> occupies on disk, excluding padding.</summary>
    /// <exception cref="FfxiTextException">The text is malformed.</exception>
    public static int MeasureBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int total = 0;
        foreach (var token in Tokenize(text))
            total += token.Length;
        return total;
    }

    /// <summary>True when <paramref name="text"/> fits in a field of <paramref name="fieldSize"/> bytes and parses cleanly.</summary>
    public static bool Fits(string text, int fieldSize)
    {
        try
        {
            return MeasureBytes(text) <= MaxTextBytes(fieldSize);
        }
        catch (FfxiTextException)
        {
            return false;
        }
    }

    /// <summary>
    /// Splits <paramref name="text"/> into the byte groups that must stay together
    /// (one byte per plain character, six bytes per auto-translate phrase).
    /// </summary>
    private static IEnumerable<byte[]> Tokenize(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == PhraseOpen)
            {
                int end = text.IndexOf(PhraseClose, i + 1);
                if (end < 0)
                    throw new FfxiTextException($"Unterminated '{PhraseOpen}' at position {i}.");

                yield return ParseAutoTranslate(text[(i + 1)..end], i);
                i = end;
                continue;
            }

            if (c != EscapeOpen)
            {
                if (c is < (char)0x20 or > (char)0x7E)
                    throw new FfxiTextException(
                        $"Character '{c}' (U+{(int)c:X4}) at position {i} is not supported by the ASCII passthrough " +
                        $"encoding. Use {{NN}} to write a raw byte.");
                yield return [(byte)c];
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == EscapeOpen)
            {
                i++;
                yield return [(byte)EscapeOpen];
                continue;
            }

            int close = text.IndexOf(EscapeClose, i + 1);
            if (close < 0)
                throw new FfxiTextException($"Unterminated '{{' escape at position {i}. Write '{{{{' for a literal brace.");

            string inner = text[(i + 1)..close];
            yield return ParseEscape(inner, i);
            i = close;
        }
    }

    private static byte[] ParseEscape(string inner, int position)
    {
        // {AT:…} is still accepted: it is typeable on any keyboard, and older exports use it.
        if (inner.StartsWith(AutoTranslatePrefix, StringComparison.OrdinalIgnoreCase))
            return ParseAutoTranslate(inner[AutoTranslatePrefix.Length..], position);

        if (inner.Length == 2 && TryParseHex(inner, out byte[]? single))
            return single;

        throw new FfxiTextException(
            $"Unknown escape '{{{inner}}}' at position {position}: expected {{NN}} (hex byte) or {PhraseOpen}phrase{PhraseClose}.");
    }

    /// <summary>
    /// Reads the inside of an auto-translate token: eight hex digits, a phrase name, an
    /// <c>item </c>-prefixed name, or a name pinned to an id with <c>#</c>.
    /// </summary>
    private static byte[] ParseAutoTranslate(string inner, int position)
    {
        {
            string body = inner.Trim();

            // Eight hex digits are always the raw payload; anything else is a phrase name.
            string hex = body.Replace("-", "", StringComparison.Ordinal);
            if (LooksLikeHexPayload(hex) && TryParseHex(hex, out byte[] payload))
                return Wrap(payload);

            byte category = AutoTranslateDictionary.PhraseCategory;
            if (body.StartsWith(ItemPrefix, StringComparison.OrdinalIgnoreCase))
            {
                category = AutoTranslateDictionary.ItemCategory;
                body = body[ItemPrefix.Length..].Trim();
            }

            // "Name#1FF2" pins the id for the names the game reuses.
            int separator = body.LastIndexOf(IdSeparator);
            if (separator >= 0)
            {
                string idText = body[(separator + 1)..];
                if (idText.Length == 4 && TryParseHex(idText, out byte[] id))
                    return Wrap(AutoTranslateDictionary.Payload(category, (ushort)((id[0] << 8) | id[1])));

                throw new FfxiTextException(
                    $"Malformed auto-translate escape '{{{inner}}}' at position {position}: expected 4 hex digits after '#'.");
            }

            var dictionary = DefaultAutoTranslate;
            if (dictionary.TryGetPayload(body, category, out byte[] fromName))
                return Wrap(fromName);

            throw new FfxiTextException(dictionary.IsAmbiguous(body, category)
                ? $"'{body}' names more than one phrase; write it as {PhraseOpen}{body}#xxxx{PhraseClose} to keep the original."
                : dictionary.IsEmpty
                    ? $"Malformed auto-translate phrase '{inner}' at position {position}: expected 8 hex digits."
                    : $"Unknown auto-translate phrase '{body}' at position {position}.");
        }
    }

    private static byte[] Wrap(ReadOnlySpan<byte> payload) =>
        [AutoTranslateMarker, payload[0], payload[1], payload[2], payload[3], AutoTranslateMarker];

    /// <summary>Eight hex digits: the raw form, and the reason a phrase named like one stays in hex.</summary>
    private static bool LooksLikeHexPayload(string text) =>
        text.Length == AutoTranslatePayloadSize * 2 && text.All(Uri.IsHexDigit);

    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return false;
        }
        return true;
    }
}
