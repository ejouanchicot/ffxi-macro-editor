using FfxiMacros.Core.Io;

namespace FfxiMacros.Core.Discovery;

/// <summary>One of the 40 macro books of a character: a title plus its 10 sets.</summary>
public sealed class BookInfo
{
    internal BookInfo(int number, CharacterFolder character)
    {
        Number = number;
        Character = character;
        Sets = Enumerable.Range(1, MacroFileNaming.SetsPerBook)
            .Select(set => new MacroSetInfo(MacroFileNaming.FileIndex(number, set), character.Path))
            .ToArray();
    }

    /// <summary>1-based book number, 1..40.</summary>
    public int Number { get; }

    public CharacterFolder Character { get; }

    public MacroSetInfo[] Sets { get; }

    /// <summary>
    /// Title from <c>mcr.ttl</c> / <c>mcr_2.ttl</c>, falling back to the game's own placeholder.
    /// </summary>
    /// <remarks>
    /// What the game shows, which is not always what the field holds. Renaming a book in game writes
    /// the new name over the old one and leaves whatever was longer sitting past the terminator — a
    /// book called <c>dncdrgGeo</c> and then cleared reads as <c>Book04{00}eo</c> on disk. The game
    /// stops at the terminator; so does this. The same rule the macros have followed all along.
    /// </remarks>
    public string Title
    {
        get
        {
            string visible = Operations.MacroRepair.VisibleInGame(Character.Titles[Number]);
            return visible.Length == 0 ? BookTitleSet.DefaultTitle(Number) : visible;
        }
        set => Character.Titles[Number] = value;
    }

    /// <summary>Everything the field holds, dead tail included — what a repair would drop.</summary>
    public string StoredTitle => Character.Titles[Number];

    /// <summary>True when the title the game shows is empty or still the <c>BookNN</c> placeholder.</summary>
    public bool IsUntitled =>
        string.Equals(Title, BookTitleSet.DefaultTitle(Number), StringComparison.Ordinal);

    public bool Exists => Sets.Any(s => s.Exists);

    public int SetCount => Sets.Count(s => s.Exists);

    public DateTime LastWriteUtc =>
        Sets.Where(s => s.Exists).Select(s => s.LastWriteUtc).DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>Set 1..10 of this book.</summary>
    public MacroSetInfo Set(int setNumber)
    {
        if (setNumber is < 1 or > MacroFileNaming.SetsPerBook)
            throw new ArgumentOutOfRangeException(nameof(setNumber), setNumber,
                $"Set must be 1..{MacroFileNaming.SetsPerBook}.");
        return Sets[setNumber - 1];
    }

    public override string ToString() => $"Book {Number}: {Title} ({SetCount} set(s))";
}
