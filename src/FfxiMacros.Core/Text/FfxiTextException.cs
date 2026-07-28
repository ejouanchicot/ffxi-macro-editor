namespace FfxiMacros.Core.Text;

/// <summary>Raised when text cannot be encoded into an FFXI text field.</summary>
public sealed class FfxiTextException : Exception
{
    public FfxiTextException(string message) : base(message) { }

    public FfxiTextException(string message, Exception inner) : base(message, inner) { }
}
