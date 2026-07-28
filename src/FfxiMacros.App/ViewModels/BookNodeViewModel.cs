using System.Globalization;
using Avalonia.Media;
using FfxiMacros.Core.Discovery;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

/// <summary>One of the 40 books of a character, holding its 10 sets.</summary>
public sealed class BookNodeViewModel : TreeNodeViewModel
{
    internal BookNodeViewModel(BookInfo info, CharacterNodeViewModel parent)
    {
        Info = info;
        Parent = parent;
        // Sets are not tree children: the game switches between them inside a book, so the UI
        // offers them as a strip of tabs rather than another level of nesting.
        Sets = info.Sets.Select(set => new SetNodeViewModel(set, this)).ToArray();
    }

    public BookInfo Info { get; }

    public CharacterNodeViewModel Parent { get; }

    public SetNodeViewModel[] Sets { get; }

    // The number moved into the chip beside the title, which leaves the eye a clean column of names.
    public override string Header => Info.Title;

    public override string Badge => Info.Number.ToString(CultureInfo.InvariantCulture);

    public override IBrush BadgeBackground => JobPalette.BackgroundFor(Info.Title);

    public override IBrush BadgeForeground => JobPalette.ForegroundFor(Info.Title);

    // Almost every book carries its ten sets, so printing "10" forty times says nothing. The
    // figure appears only for the books that are partly filled — the ones worth noticing.
    public override string Trailing =>
        Info.SetCount is 0 or 10 ? "" : Info.SetCount.ToString(CultureInfo.InvariantCulture);

    public override double RowOpacity => Info.Exists ? 1.0 : 0.5;

    public override string Detail
    {
        get
        {
            int sets = Info.SetCount;
            return sets == 0 ? Loc.T("Tree.BookEmpty")
                 : sets == 1 ? Loc.T("Tree.SetOne")
                 : Loc.T("Tree.SetMany", sets);
        }
    }

    public override bool IsDirty => Sets.Any(s => s.IsDirty);

    /// <summary>True when the book has no set file on disk and still carries its placeholder title.</summary>
    public bool IsEmptyAndUntitled => !Info.Exists && Info.IsUntitled;

    /// <summary>Renames the book and writes the title file half that holds it.</summary>
    public void Rename(string title)
    {
        Info.Title = title;
        Parent.Character.Titles.SaveHalfFor(Info.Number);
        RefreshLabels();
    }

    /// <summary>Pushes a change up to the book and character labels, so dirty markers appear.</summary>
    internal void RefreshUpwards()
    {
        RefreshLabels();
        Parent.RefreshLabels();
    }
}
