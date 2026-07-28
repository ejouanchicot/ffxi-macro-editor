namespace FfxiMacros.Core.Model;

/// <summary>
/// A single macro slot: a short name plus six command lines.
/// </summary>
public sealed class Macro
{
    public const int LineCount = 6;

    /// <summary>
    /// The four reserved/flag bytes that open the macro record on disk.
    /// Always <c>00 00 00 00</c> in every observed file; preserved verbatim so we never invent a value.
    /// </summary>
    public byte[] Header { get; } = new byte[4];

    /// <summary>
    /// The reserved byte that closes the macro record on disk.
    /// Always <c>0x00</c> in every observed file; preserved verbatim.
    /// </summary>
    public byte Trailer { get; set; }

    /// <summary>Macro name, in the editable text form produced by <see cref="Text.FfxiText"/> (max 8 bytes encoded).</summary>
    public string Name { get; set; } = "";

    /// <summary>The six command lines, in editable text form (max 60 bytes encoded each).</summary>
    public string[] Lines { get; } = new string[LineCount];

    public Macro()
    {
        for (int i = 0; i < LineCount; i++)
            Lines[i] = "";
    }

    public bool IsEmpty =>
        string.IsNullOrEmpty(Name) && Lines.All(string.IsNullOrEmpty);

    public Macro Clone()
    {
        var copy = new Macro { Name = Name, Trailer = Trailer };
        Header.CopyTo(copy.Header, 0);
        for (int i = 0; i < LineCount; i++)
            copy.Lines[i] = Lines[i];
        return copy;
    }

    public void Clear()
    {
        Name = "";
        for (int i = 0; i < LineCount; i++)
            Lines[i] = "";
    }

    public override string ToString() =>
        IsEmpty ? "(empty)" : $"{Name}: {string.Join(" | ", Lines.Where(l => l.Length > 0))}";
}
