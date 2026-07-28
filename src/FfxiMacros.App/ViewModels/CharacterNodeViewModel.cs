using System.Globalization;
using Avalonia.Media;
using FfxiMacros.Core.Discovery;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

/// <summary>A character folder in the tree.</summary>
public sealed class CharacterNodeViewModel : TreeNodeViewModel
{
    private readonly List<BookNodeViewModel> _allBooks;
    private bool _showEmptyBooks;

    internal CharacterNodeViewModel(CharacterFolder character, bool showEmptyBooks)
    {
        Character = character;
        _showEmptyBooks = showEmptyBooks;
        _allBooks = character.Books.Select(b => new BookNodeViewModel(b, this)).ToList();
        ApplyBookFilter();
    }

    public CharacterFolder Character { get; }

    public IEnumerable<BookNodeViewModel> Books => _allBooks;

    public override string Header => Character.Label;

    /// <summary>The first two letters of the name, as an avatar would carry them.</summary>
    public override string Badge =>
        Character.Label.Length >= 2 ? Character.Label[..2].ToUpperInvariant() : Character.Label.ToUpperInvariant();

    public override string Trailing => Character.BookCount.ToString(CultureInfo.InvariantCulture);

    public override FontWeight HeaderWeight => FontWeight.SemiBold;

    public override string Detail
    {
        get
        {
            string counts = Loc.T("Tree.CharacterDetail",
                Character.BookCount == 1 ? Loc.T("Tree.BookOne") : Loc.T("Tree.BookMany", Character.BookCount),
                Character.SetFileCount == 1 ? Loc.T("Tree.SetOne") : Loc.T("Tree.SetMany", Character.SetFileCount));

            return Character.SkippedFiles.Count == 0
                ? counts
                : $"{counts} · {Loc.T("Tree.Skipped", Character.SkippedFiles.Count)}";
        }
    }

    /// <summary>When false, books with no set file and no title are hidden to keep the tree short.</summary>
    public bool ShowEmptyBooks
    {
        get => _showEmptyBooks;
        set
        {
            if (SetField(ref _showEmptyBooks, value))
                ApplyBookFilter();
        }
    }

    /// <summary>Sets a readable name for the folder; persisting it is the caller's job.</summary>
    public void Rename(string? displayName)
    {
        Character.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        RefreshLabels();
    }

    private void ApplyBookFilter()
    {
        var wanted = _showEmptyBooks
            ? _allBooks
            : _allBooks.Where(b => !b.IsEmptyAndUntitled).ToList();

        Children.Clear();
        foreach (var book in wanted)
            Children.Add(book);
    }
}
