using System.Collections.ObjectModel;
using Avalonia.Media;

namespace FfxiMacros.App.ViewModels;

/// <summary>Common shape for the characters / books / sets tree, so one template renders all three.</summary>
public abstract class TreeNodeViewModel : ViewModelBase
{
    private bool _isExpanded;

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

    /// <summary>A single figure at the end of the row: how many sets, how many books.</summary>
    public virtual string Trailing => "";

    /// <summary>Dimmed for a book the game has never written a set file for.</summary>
    public virtual double RowOpacity => 1.0;

    /// <summary>Characters carry more weight than the books listed under them.</summary>
    public virtual FontWeight HeaderWeight => FontWeight.Normal;

    /// <summary>True when this node or anything below it has unsaved edits.</summary>
    public virtual bool IsDirty => Children.Any(c => c.IsDirty);

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
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
