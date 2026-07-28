using System.Globalization;
using Avalonia.Media;
using FfxiMacros.Core.Discovery;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

/// <summary>A character folder in the tree.</summary>
public sealed class CharacterNodeViewModel : TreeNodeViewModel
{
    private readonly List<BookNodeViewModel> _allBooks;

    /// <summary>
    /// Lists all 40 books, always.
    /// </summary>
    /// <remarks>
    /// The empty ones used to be hidden behind a checkbox. A character has forty book slots whether
    /// or not the game ever wrote a file for one — hiding the empty ones made a book vanish the
    /// moment it was emptied, and made « put this one on book 12 » a two-step affair.
    /// </remarks>
    internal CharacterNodeViewModel(CharacterFolder character)
    {
        Character = character;
        _allBooks = character.Books.Select(b => new BookNodeViewModel(b, this)).ToList();

        foreach (var book in _allBooks)
            Children.Add(book);
    }

    public CharacterFolder Character { get; }

    public IEnumerable<BookNodeViewModel> Books => _allBooks;

    public override string Header => Character.Label;

    /// <summary>The first two letters of the name, as an avatar would carry them.</summary>
    public override string Badge =>
        Character.Label.Length >= 2 ? Character.Label[..2].ToUpperInvariant() : Character.Label.ToUpperInvariant();

    public override string Trailing => Character.BookCount.ToString(CultureInfo.InvariantCulture);

    public override FontWeight HeaderWeight => FontWeight.SemiBold;

    /// <summary>A folder named after a number is worth renaming; it is also what links a live report.</summary>
    public override bool CanRename => true;

    public override string Detail
    {
        get
        {
            string counts = Loc.T("Tree.CharacterDetail",
                Character.BookCount == 1 ? Loc.T("Tree.BookOne") : Loc.T("Tree.BookMany", Character.BookCount),
                Character.SetFileCount == 1 ? Loc.T("Tree.SetOne") : Loc.T("Tree.SetMany", Character.SetFileCount));

            if (Character.SkippedFiles.Count > 0)
                counts = $"{counts} · {Loc.T("Tree.Skipped", Character.SkippedFiles.Count)}";

            // A folder renamed to something readable is invisible to the game, which then starts
            // the character again from empty macros. Worth saying out loud, right on the row.
            return Character.HasHexId ? counts : $"{counts} · {Loc.T("Tree.NotHexFolder")}";
        }
    }

    /// <summary>True when the folder name is not the hexadecimal one the game gave it.</summary>
    public bool IsUnreachableByGame => !Character.HasHexId;

    /// <summary>Sets a readable name for the folder; persisting it is the caller's job.</summary>
    public void Rename(string? displayName)
    {
        Character.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        RefreshLabels();
    }

}
