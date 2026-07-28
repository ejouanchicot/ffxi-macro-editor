using System.Text;

namespace FfxiMacros.Core.Discovery;

/// <summary>
/// Minimal reader for Valve's KeyValues text format, enough to walk <c>libraryfolders.vdf</c>.
/// </summary>
/// <remarks>
/// The format is a tree of quoted strings: a key followed either by a quoted value or by a
/// brace-delimited block. Only what Steam actually writes is supported; anything unparseable
/// yields an empty node rather than an exception, because a malformed Steam file must not stop
/// the editor from starting.
/// </remarks>
public sealed class ValveKeyValues
{
    private readonly Dictionary<string, ValveKeyValues> _children = new(StringComparer.OrdinalIgnoreCase);

    public string? Value { get; private set; }

    public IReadOnlyDictionary<string, ValveKeyValues> Children => _children;

    /// <summary>Child node by key, or an empty node when absent, so lookups can be chained safely.</summary>
    public ValveKeyValues this[string key] =>
        _children.TryGetValue(key, out var child) ? child : Empty;

    public static ValveKeyValues Empty { get; } = new();

    public static ValveKeyValues Parse(string text)
    {
        var root = new ValveKeyValues();
        int position = 0;
        try
        {
            ParseInto(root, text, ref position, depth: 0);
        }
        catch (FormatException)
        {
            return root;   // Keep whatever parsed cleanly.
        }
        return root;
    }

    public static ValveKeyValues ParseFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    private static void ParseInto(ValveKeyValues node, string text, ref int position, int depth)
    {
        if (depth > 32)
            throw new FormatException("KeyValues nesting is too deep.");

        while (true)
        {
            SkipTrivia(text, ref position);
            if (position >= text.Length)
                return;

            if (text[position] == '}')
            {
                position++;
                return;
            }

            string key = ReadToken(text, ref position);
            SkipTrivia(text, ref position);
            if (position >= text.Length)
                return;

            // Attach before descending, so a truncated file still yields everything parsed so far.
            var child = new ValveKeyValues();
            node._children[key] = child;

            if (text[position] == '{')
            {
                position++;
                ParseInto(child, text, ref position, depth + 1);
            }
            else
            {
                child.Value = ReadToken(text, ref position);
            }
        }
    }

    private static void SkipTrivia(string text, ref int position)
    {
        while (position < text.Length)
        {
            char c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
            }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] is not ('\n' or '\r'))
                    position++;
            }
            else
            {
                return;
            }
        }
    }

    private static string ReadToken(string text, ref int position)
    {
        if (text[position] != '"')
        {
            int start = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}'))
                position++;
            return text[start..position];
        }

        position++;   // opening quote
        var sb = new StringBuilder();
        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
                sb.Append(text[position] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    _ => text[position],     // \\ and \" collapse to the literal character
                });
            }
            else
            {
                sb.Append(text[position]);
            }
            position++;
        }

        if (position >= text.Length)
            throw new FormatException("Unterminated quoted string.");

        position++;   // closing quote
        return sb.ToString();
    }
}
