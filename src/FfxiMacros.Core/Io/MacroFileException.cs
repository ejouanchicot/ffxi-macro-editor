namespace FfxiMacros.Core.Io;

/// <summary>
/// Raised when a macro file is not in the expected format, or cannot be written safely.
/// Always carries a message meant to be shown to the user — the old tool swallowed these.
/// </summary>
public sealed class MacroFileException : Exception
{
    public MacroFileException(string message) : base(message) { }

    public MacroFileException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Path involved, when the failure came from a file on disk.</summary>
    public string? Path { get; init; }

    public override string ToString() =>
        Path is null ? base.ToString() : $"{Message} (file: {Path})";
}
