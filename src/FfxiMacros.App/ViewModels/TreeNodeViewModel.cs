using System.Collections.ObjectModel;
using Avalonia.Media;

namespace FfxiMacros.App.ViewModels;

/// <summary>Common shape for the characters / books / sets tree, so one template renders all three.</summary>
public abstract class TreeNodeViewModel : ViewModelBase
{
    private bool _isExpanded;
    private bool _isRenaming;
    private string _renameDraft = "";

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    /// <summary>Main label, e.g. <c>Kaelith</c>, <c>3 · CorDnc</c> or <c>Set 4</c>.</summary>
    public abstract string Header { get; }

    /// <summary>Dimmed text after the header: counts, dates, warnings.</summary>
    public abstract string Detail { get; }

    /// <summary>Text of the chip at the head of the row — a book number, a character's initials.</summary>
    public virtual string Badge => "";

    /// <summary>Fill of that chip. Books tint it by job, so the list can be scanned by colour.</summary>
    public virtual IBrush BadgeBackground => JobPalette.BackgroundFor(null);

    public virtual IBrush BadgeForeground => JobPalette.ForegroundFor(null);

    /// <summary>The tint laid over a book's card, from the role of the job it leads with.</summary>
    public virtual IBrush RowWash => Brushes.Transparent;

    /// <summary>A single figure at the end of the row: how many sets, how many books.</summary>
    public virtual string Trailing => "";

    /// <summary>Dimmed for a book the game has never written a set file for.</summary>
    public virtual double RowOpacity => 1.0;

    /// <summary>Marks the book the game has this character on; never true for a character row.</summary>
    public virtual bool IsOpenInGame => false;

    /// <summary>
    /// True for a book, which the list draws as a card you press. A character is the heading above
    /// its forty of them, and reads as one rather than competing with them.
    /// </summary>
    public virtual bool IsBook => false;

    /// <summary>Characters carry more weight than the books listed under them.</summary>
    public virtual FontWeight HeaderWeight => FontWeight.Normal;

    /// <summary>True when this node or anything below it has unsaved edits.</summary>
    public virtual bool IsDirty => Children.Any(c => c.IsDirty);

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>
    /// The book whose macros are on screen.
    /// </summary>
    /// <remarks>
    /// Carried by the node rather than read from the tree's selection: a style keyed on a selected
    /// item also matches everything inside it, so selecting a character lit all forty of its books
    /// at once. A row that knows whether it is the one being edited cannot make that mistake.
    /// </remarks>
    public bool IsCurrent
    {
        get => _isCurrent;
        internal set => SetField(ref _isCurrent, value);
    }

    private bool _isCurrent;

    /// <summary>
    /// Books carry a title of their own, written to <c>mcr.ttl</c> and read by the game; a character
    /// is a folder on disk and has no name to change here.
    /// </summary>
    public virtual bool CanRename => false;

    /// <summary>True while the row shows a text box instead of its label.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        internal set => SetField(ref _isRenaming, value);
    }

    /// <summary>The name being typed, kept apart from the stored one until it is committed.</summary>
    public string RenameDraft
    {
        get => _renameDraft;
        set => SetField(ref _renameDraft, value);
    }

    /// <summary>Re-raises the properties that change when an edit happens somewhere below.</summary>
    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Badge));
        OnPropertyChanged(nameof(BadgeBackground));
        OnPropertyChanged(nameof(BadgeForeground));
        OnPropertyChanged(nameof(Trailing));
    }
}
