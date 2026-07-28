namespace FfxiMacros.Core.Model;

/// <summary>
/// One <c>mcr*.dat</c> file: 20 macro slots (Ctrl-1..Ctrl-0, then Alt-1..Alt-0).
/// </summary>
/// <remarks>
/// In FFXI terms a file is one <em>macro set</em>; ten consecutive sets form a <em>book</em>
/// (see <see cref="Io.MacroFileNaming"/>). The name "MacroBook" is kept for continuity with the
/// original tool and the spec.
/// </remarks>
public sealed class MacroBook
{
    public const int MacroCount = 20;

    /// <summary>
    /// The 8-byte file version/stamp from the header. Copied through untouched on save:
    /// the low word is always 1, the high word varies per install and its meaning is unconfirmed.
    /// </summary>
    public ulong Version { get; set; }

    public Macro[] Macros { get; } = new Macro[MacroCount];

    /// <summary>Path this book was loaded from, when it came from disk.</summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// False when the MD5 stored in the header did not match the data block at load time
    /// (the file was written by a buggy tool, or is corrupt). The digest is always recomputed on save.
    /// </summary>
    public bool DigestWasValid { get; set; } = true;

    public MacroBook()
    {
        for (int i = 0; i < MacroCount; i++)
            Macros[i] = new Macro();
    }

    public bool IsEmpty => Macros.All(m => m.IsEmpty);

    public MacroBook Clone()
    {
        var copy = new MacroBook
        {
            Version = Version,
            SourcePath = SourcePath,
            DigestWasValid = DigestWasValid,
        };
        for (int i = 0; i < MacroCount; i++)
            copy.Macros[i] = Macros[i].Clone();
        return copy;
    }
}
