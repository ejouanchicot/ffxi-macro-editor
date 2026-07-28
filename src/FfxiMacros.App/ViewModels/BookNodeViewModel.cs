using System.Globalization;
using Avalonia.Media;
using FfxiMacros.Core.Discovery;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

/// <summary>How the editor came to know which book a character has open, best first.</summary>
public enum OpenBookSource
{
    /// <summary>From the files: where the game recorded the character when it saved its state.</summary>
    File,

    /// <summary>From the Windower addon, which hears the <c>/macro book</c> commands go past.</summary>
    Windower,

    /// <summary>From the client's own memory — true whatever the player did to get there.</summary>
    Memory,
}

/// <summary>One of the 40 books of a character, holding its 10 sets.</summary>
public sealed class BookNodeViewModel : TreeNodeViewModel
{
    private bool _isOpenInGame;

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

    /// <summary>True for the book the character is on, reported live or recorded in <c>mcr.sys</c>.</summary>
    public override bool IsOpenInGame => _isOpenInGame;

    /// <summary>Where that came from, which is what the tooltip explains.</summary>
    private OpenBookSource _source;

    internal void SetOpenInGame(bool value, OpenBookSource source)
    {
        if (_isOpenInGame == value && _source == source)
            return;

        _isOpenInGame = value;
        _source = source;
        OnPropertyChanged(nameof(IsOpenInGame));
        OnPropertyChanged(nameof(Detail));
    }

    public override string Detail
    {
        get
        {
            int sets = Info.SetCount;
            string counts = sets == 0 ? Loc.T("Tree.BookEmpty")
                          : sets == 1 ? Loc.T("Tree.SetOne")
                          : Loc.T("Tree.SetMany", sets);

            // Nothing is hidden without saying so: a title the game overwrote with a shorter one
            // still carries the tail of the old one, which only a rename will clear.
            if (Core.Operations.MacroRepair.IsDamaged(Info.StoredTitle))
                counts = $"{counts} · {Loc.T("Tree.TitleTail")}";

            if (!IsOpenInGame)
                return counts;

            return $"{counts} · {Loc.T(_source switch
            {
                OpenBookSource.Memory => "Tree.OpenInGameMemory",
                OpenBookSource.Windower => "Tree.OpenInGameLive",
                _ => "Tree.OpenInGame",
            })}";
        }
    }

    public override bool IsDirty => Sets.Any(s => s.IsDirty);

    public override bool CanRename => true;

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
