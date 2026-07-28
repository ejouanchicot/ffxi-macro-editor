using FfxiMacros.Core.Model;

namespace FfxiMacros.Core.Serialization;

/// <summary>
/// One set of a book inside an exported file.
/// </summary>
/// <param name="SetNumber">
/// Which of the book's ten sets this is, or 0 when the file does not say — the shape of a
/// single-set export, and of every file written before books could be exported whole.
/// </param>
/// <param name="Book">The twenty macros of that set.</param>
public sealed record MacroSetExport(int SetNumber, MacroBook Book);
