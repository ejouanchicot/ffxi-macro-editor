using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using FfxiMacros.Core.Operations;
using FfxiMacros.Core.Text;

namespace FfxiMacros.App.ViewModels;

/// <summary>Measures an edited field against what actually fits on disk.</summary>
internal static class TextField
{
    public static (int Bytes, string? Error) Measure(string text, int fieldSize)
    {
        int max = FfxiText.MaxTextBytes(fieldSize);
        try
        {
            int bytes = FfxiText.MeasureBytes(text);
            return (bytes, bytes > max ? $"{bytes} octets pour {max} disponibles." : null);
        }
        catch (FfxiTextException ex)
        {
            return (0, ex.Message);
        }
    }
}

/// <summary>One of the six command lines of a macro.</summary>
public sealed class MacroLineViewModel : ViewModelBase
{
    private readonly MacroSlotViewModel _slot;
    private readonly int _index;

    internal MacroLineViewModel(MacroSlotViewModel slot, int index)
    {
        _slot = slot;
        _index = index;
        RepairCommand = new RelayCommand(Repair, () => HasHiddenBytes);
    }

    public string Label => $"{_index + 1}";

    public int MaxBytes => MacroBookFile.MaxLineBytes;

    /// <summary>Everything the field holds, dead bytes included.</summary>
    private string Stored => _slot.Macro.Lines[_index];

    /// <summary>
    /// The line as the game runs it. FFXI stops reading at the first NUL, so a field holding
    /// <c>/ta &lt;stpc&gt;{00}" &lt;t&gt;</c> is really just <c>/ta &lt;stpc&gt;</c> — and that is what is shown
    /// and edited here. What lies beyond is reported by <see cref="HiddenText"/> rather than mixed
    /// into the command.
    /// </summary>
    public string Text
    {
        get => MacroRepair.VisibleInGame(Stored);
        set
        {
            // Compared against what is displayed, so merely tabbing through a damaged line
            // does not count as an edit.
            if (Text == (value ?? ""))
                return;

            _slot.Macro.Lines[_index] = value ?? "";
            OnPropertyChanged();
            Revalidate();
            _slot.OnLineEdited();
        }
    }

    /// <summary>True when the field holds bytes the game will never read.</summary>
    public bool HasHiddenBytes => MacroRepair.IsDamaged(Stored);

    /// <summary>The dead tail, shown so nothing is silently hidden from the user.</summary>
    public string HiddenText
    {
        get
        {
            string visible = Text;
            return Stored.Length > visible.Length ? Stored[visible.Length..] : "";
        }
    }

    public string HiddenWarning =>
        $"Le jeu ignore la suite : {HiddenText}";

    /// <summary>Writes back the line the game actually runs, dropping the dead bytes.</summary>
    public RelayCommand RepairCommand { get; }

    public int ByteCount { get; private set; }

    public string? Error { get; private set; }

    public bool HasError => Error is not null;

    public string Counter => $"{ByteCount}/{MaxBytes}";

    private void Repair()
    {
        // Written straight to the model: the repaired text usually equals what is already displayed,
        // so going through the setter would see no change and leave the dead bytes in place.
        // TryRepair also restores the leading '/' an older tool overwrote, when there was one.
        _slot.Macro.Lines[_index] = MacroRepair.TryRepair(Stored, out string repaired, out _)
            ? repaired
            : Text;

        NotifyTextReplaced();
        _slot.OnLineEdited();
    }

    internal void Revalidate()
    {
        (ByteCount, Error) = TextField.Measure(Text, MacroBookFile.LineFieldSize);
        OnPropertyChanged(nameof(ByteCount));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Counter));
        OnPropertyChanged(nameof(HasHiddenBytes));
        OnPropertyChanged(nameof(HiddenText));
        OnPropertyChanged(nameof(HiddenWarning));
        RepairCommand.RaiseCanExecuteChanged();
    }

    internal void NotifyTextReplaced()
    {
        OnPropertyChanged(nameof(Text));
        Revalidate();
    }
}

/// <summary>One of the 20 macro slots of a set: a name and six lines.</summary>
public sealed class MacroSlotViewModel : ViewModelBase
{
    private readonly SetNodeViewModel _set;
    private bool _isSelected;

    internal MacroSlotViewModel(SetNodeViewModel set, int index)
    {
        _set = set;
        Index = index;
        Lines = Enumerable.Range(0, Macro.LineCount).Select(i => new MacroLineViewModel(this, i)).ToArray();
        ClearCommand = new RelayCommand(Clear, () => !IsEmpty);
        RepairNameCommand = new RelayCommand(RepairName, () => HasHiddenName);
        Revalidate();
    }

    /// <summary>The underlying macro, used by the copy/move operations.</summary>
    public Macro Macro => _set.Loaded!.Macros[Index];

    public int Index { get; }

    /// <summary><c>Ctrl-1</c> … <c>Alt-0</c>.</summary>
    public string SlotLabel => MacroSlot.Describe(Index);

    public MacroLineViewModel[] Lines { get; }

    public RelayCommand ClearCommand { get; }

    public int MaxNameBytes => MacroBookFile.MaxNameBytes;

    /// <summary>
    /// The name as the game shows it — cut at the first NUL, exactly like <see cref="DisplayName"/>.
    /// A stored <c>Palis{00}el</c> reads and edits as <c>Palis</c>; the dead bytes are reported by
    /// <see cref="HiddenNameText"/> instead of being mixed into the name.
    /// </summary>
    public string Name
    {
        get => MacroRepair.VisibleInGame(Macro.Name);
        set
        {
            if (Name == (value ?? ""))
                return;

            Macro.Name = value ?? "";
            OnPropertyChanged();
            Revalidate();
            OnLineEdited();
        }
    }

    /// <summary>True when the stored name holds bytes the game will never read.</summary>
    public bool HasHiddenName => MacroRepair.IsDamaged(Macro.Name);

    public string HiddenNameText
    {
        get
        {
            string visible = Name;
            return Macro.Name.Length > visible.Length ? Macro.Name[visible.Length..] : "";
        }
    }

    public string HiddenNameWarning => $"Le jeu ignore la suite du nom : {HiddenNameText}";

    /// <summary>Writes back the name the game actually shows, dropping the dead bytes.</summary>
    public RelayCommand RepairNameCommand { get; }

    private void RepairName()
    {
        // Straight to the model, for the same reason as a line: the repaired text usually equals
        // what is already displayed.
        Macro.Name = MacroRepair.TryRepair(Macro.Name, out string repaired, out _) ? repaired : Name;
        NotifyMacroReplaced();
        OnLineEdited();
    }

    public int NameByteCount { get; private set; }

    public string? NameError { get; private set; }

    public bool HasNameError => NameError is not null;

    public string NameCounter => $"{NameByteCount}/{MaxNameBytes}";

    /// <summary>
    /// Name shown on the palette button — what the game itself displays, so a damaged name such as
    /// <c>Palis{00}el</c> reads as <c>Palis</c> here just as it does in FFXI. The editable field
    /// below still holds every byte, and « Réparer » writes the shortened form back.
    /// </summary>
    public string DisplayName =>
        MacroRepair.VisibleInGame(Macro.Name) is { Length: > 0 } visible ? visible : "—";

    /// <summary>True when a field holds bytes past the NUL that the game will never read.</summary>
    public bool IsDamaged =>
        MacroRepair.IsDamaged(Macro.Name) || Macro.Lines.Any(MacroRepair.IsDamaged);

    /// <summary>Dims the buttons of empty slots, the way the game greys out unused macros.</summary>
    public double NameOpacity => IsEmpty ? 0.35 : 1.0;

    /// <summary>Drives the highlight on the palette button.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }

    /// <summary>First line the game would actually run, for the button tooltip.</summary>
    public string Preview =>
        Macro.Lines.Select(MacroRepair.VisibleInGame).FirstOrDefault(l => l.Length > 0) ?? "";

    public bool IsEmpty => Macro.IsEmpty;

    /// <summary>True when any field would fail to encode; blocks saving.</summary>
    public bool HasError => HasNameError || Lines.Any(l => l.HasError);

    public void Clear()
    {
        Macro.Clear();
        NotifyMacroReplaced();
        OnLineEdited();
    }

    /// <summary>Tells the set an edit happened, refreshing the palette label and the dirty marker.</summary>
    public void OnLineEdited()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsDamaged));
        OnPropertyChanged(nameof(HasError));
        ClearCommand.RaiseCanExecuteChanged();
        _set.MarkDirty();
    }

    /// <summary>Re-reads every field from the model, after a reload, a paste or a clear.</summary>
    public void NotifyMacroReplaced()
    {
        OnPropertyChanged(nameof(Name));
        foreach (var line in Lines)
            line.NotifyTextReplaced();

        Revalidate();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsDamaged));
        OnPropertyChanged(nameof(HasError));
        ClearCommand.RaiseCanExecuteChanged();
    }

    private void Revalidate()
    {
        (NameByteCount, NameError) = TextField.Measure(Name, MacroBookFile.NameFieldSize);
        OnPropertyChanged(nameof(NameByteCount));
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(NameCounter));
        OnPropertyChanged(nameof(HasHiddenName));
        OnPropertyChanged(nameof(HiddenNameText));
        OnPropertyChanged(nameof(HiddenNameWarning));
        RepairNameCommand.RaiseCanExecuteChanged();

        foreach (var line in Lines)
            line.Revalidate();
    }
}
