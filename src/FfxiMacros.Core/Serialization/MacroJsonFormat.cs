using System.Text.Json;
using System.Text.Json.Serialization;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;

namespace FfxiMacros.Core.Serialization;

/// <summary>One macro slot in a JSON export.</summary>
public sealed class MacroDocumentSlot
{
    public int Index { get; set; }

    /// <summary><c>Ctrl-1</c> … <c>Alt-0</c>, for readability; <see cref="Index"/> is what is read back.</summary>
    public string? Key { get; set; }

    public string Name { get; set; } = "";

    public List<string> Lines { get; set; } = [];
}

/// <summary>A macro set in a JSON export.</summary>
public sealed class MacroDocumentSet
{
    public int Set { get; set; }

    public List<MacroDocumentSlot> Macros { get; set; } = [];
}

/// <summary>A set or a whole book, as exchanged on disk.</summary>
public sealed class MacroDocument
{
    public string Format { get; set; } = MacroJsonFormat.FormatName;

    public int Version { get; set; } = 1;

    public string? Character { get; set; }

    public int? Book { get; set; }

    public string? Title { get; set; }

    public List<MacroDocumentSet> Sets { get; set; } = [];
}

/// <summary>
/// Structured export of a set or a book, for versioning macros or moving them between characters.
/// The text form of every field is the same escaping the editor shows, so an export re-imports into
/// identical bytes.
/// </summary>
public static class MacroJsonFormat
{
    public const string FormatName = "ffxi-macros";
    public const string FileExtension = ".json";

    /// <summary>
    /// Serialisation goes through the generated context rather than reflection: reflection cannot
    /// survive trimming — nothing in the code tells the trimmer these properties are read from a
    /// file — and reading the shape at build time is faster besides.
    /// </summary>
    /// <remarks>
    /// The relaxed encoder matters here: macro lines are full of <c>&lt;me&gt;</c> and quotes, and
    /// the default encoder would write them back as <c><</c> escapes.
    /// </remarks>
    private static readonly MacroJsonContext Context = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static MacroDocumentSet ToDocument(MacroBook book, int setNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(book);

        var document = new MacroDocumentSet { Set = setNumber };
        for (int index = 0; index < MacroBook.MacroCount; index++)
        {
            var macro = book.Macros[index];
            if (macro.IsEmpty)
                continue;

            // Up to the last used line, gaps included, so line positions survive the round trip.
            int last = MacroTextFormat.LastUsedLine(macro);

            document.Macros.Add(new MacroDocumentSlot
            {
                Index = index,
                Key = MacroSlot.Describe(index),
                Name = macro.Name,
                Lines = macro.Lines.Take(last + 1).ToList(),
            });
        }

        return document;
    }

    public static string Export(MacroBook book, string? character = null, int? bookNumber = null, string? title = null, int setNumber = 1)
    {
        var document = new MacroDocument
        {
            Character = character,
            Book = bookNumber,
            Title = title,
            Sets = [ToDocument(book, setNumber)],
        };

        return JsonSerializer.Serialize(document, Context.MacroDocument);
    }

    public static string Export(MacroDocument document) => JsonSerializer.Serialize(document, Context.MacroDocument);

    /// <exception cref="MacroFileException">The JSON is malformed or not a macro export.</exception>
    public static MacroDocument Parse(string json)
    {
        MacroDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, Context.MacroDocument);
        }
        catch (JsonException ex)
        {
            throw new MacroFileException($"This file is not readable JSON: {ex.Message}", ex);
        }

        if (document is null)
            throw new MacroFileException("This file is empty.");
        if (!string.Equals(document.Format, FormatName, StringComparison.OrdinalIgnoreCase))
            throw new MacroFileException($"This is not an FFXI macro export (format = '{document.Format}').");

        return document;
    }

    /// <summary>Applies one exported set onto a book. Slots absent from the document are left alone.</summary>
    public static MacroBook Apply(MacroDocumentSet document, MacroBook? into = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var book = into ?? new MacroBook { Version = 1 };

        foreach (var slot in document.Macros)
        {
            if (slot.Index is < 0 or >= MacroBook.MacroCount)
                throw new MacroFileException($"Macro index {slot.Index} is outside 0..{MacroBook.MacroCount - 1}.");
            if (slot.Lines.Count > Macro.LineCount)
                throw new MacroFileException(
                    $"Macro {MacroSlot.Describe(slot.Index)} has {slot.Lines.Count} lines, {Macro.LineCount} maximum.");

            var macro = book.Macros[slot.Index];
            macro.Clear();
            macro.Name = slot.Name ?? "";
            for (int i = 0; i < slot.Lines.Count; i++)
                macro.Lines[i] = slot.Lines[i] ?? "";
        }

        return book;
    }

    /// <summary>Reads a single-set export straight into a book.</summary>
    public static MacroBook Import(string json, MacroBook? into = null)
    {
        var document = Parse(json);
        if (document.Sets.Count == 0)
            throw new MacroFileException("This export contains no set.");

        return Apply(document.Sets[0], into);
    }
}
