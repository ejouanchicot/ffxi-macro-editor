using System.Collections.ObjectModel;
using System.Globalization;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using FfxiMacros.Core.Operations;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

/// <summary>A macro set (one <c>mcr*.dat</c>) in the tree: loads on demand, tracks unsaved edits.</summary>
public sealed class SetNodeViewModel : TreeNodeViewModel
{
    private bool _isDirty;
    private bool _isCurrent;

    internal SetNodeViewModel(MacroSetInfo info, BookNodeViewModel parent)
    {
        Info = info;
        Parent = parent;
    }

    public MacroSetInfo Info { get; }

    public BookNodeViewModel Parent { get; }

    /// <summary>Raised on every edit, save or reload, so the toolbar can refresh its state.</summary>
    internal Action? Changed { get; set; }

    /// <summary>The file's contents, null until the set is opened for the first time.</summary>
    internal MacroBook? Loaded { get; private set; }

    /// <summary>The 20 macro slots, populated once the set has been loaded.</summary>
    public ObservableCollection<MacroSlotViewModel> Macros { get; } = [];

    public override string Header => Loc.T("Tree.Set", Info.SetNumber);

    public override string Detail => !Info.Exists
        ? Loc.T("Tree.SetNew")
        : !Info.HasExpectedSize
            ? Loc.T("Tree.SetBadSize", Info.SizeBytes)
            : Info.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>Dims the tab of a set the game has never written.</summary>
    public double TabOpacity => Info.Exists ? 1.0 : 0.45;

    /// <summary>
    /// True for set 1, the set players anchor on: the game's up arrow from there reaches set 10 and
    /// the down arrow set 2, so it gets a frame of its own in the set column.
    /// </summary>
    public bool IsHome => Info.SetNumber == 1;

    /// <summary>Drives the highlight on the set tab.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        internal set => SetField(ref _isCurrent, value);
    }

    public override bool IsDirty => _isDirty;

    public bool IsLoaded => Loaded is not null;

    /// <summary>True when a macro holds text that cannot be written back; blocks saving.</summary>
    public bool HasError => Macros.Any(m => m.HasError);

    /// <summary>Reads the file, or starts from an empty set when the file does not exist yet.</summary>
    public void Load()
    {
        Loaded = ReadOrCreate();
        Macros.Clear();
        for (int i = 0; i < MacroBook.MacroCount; i++)
            Macros.Add(new MacroSlotViewModel(this, i));

        _isDirty = false;
        RefreshLabels();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>Throws away unsaved edits and re-reads the file.</summary>
    public void Reload()
    {
        if (!IsLoaded)
            return;

        Loaded = ReadOrCreate();
        foreach (var slot in Macros)
            slot.NotifyMacroReplaced();

        _isDirty = false;
        RefreshLabels();
        Parent.RefreshUpwards();
    }

    public void Save()
    {
        if (Loaded is null)
            return;

        DropDeadBytes();
        Info.Save(Loaded);   // refreshes the file's size and timestamp

        _isDirty = false;
        RefreshLabels();
        Parent.RefreshUpwards();
    }

    /// <summary>Marks the set as holding unsaved edits.</summary>
    public void MarkDirty()
    {
        _isDirty = true;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasError));
        RefreshLabels();
        Parent.RefreshUpwards();
        Changed?.Invoke();
    }

    /// <summary>
    /// Writes back exactly what the editor shows: the bytes the old 2014 editor left after the
    /// terminator are dropped, so the file stops carrying rubbish the game never reads.
    /// </summary>
    /// <remarks>
    /// A field whose <em>first</em> byte is the terminator is left untouched. The game runs nothing
    /// there, so there is no rubbish to remove — only text that « Réparer » can still bring back, and
    /// throwing it away silently would lose it for good.
    /// </remarks>
    private void DropDeadBytes()
    {
        if (Loaded is null)
            return;

        foreach (var macro in Loaded.Macros)
        {
            macro.Name = WithoutDeadBytes(macro.Name);
            for (int line = 0; line < Macro.LineCount; line++)
                macro.Lines[line] = WithoutDeadBytes(macro.Lines[line]);
        }
    }

    private static string WithoutDeadBytes(string text)
    {
        string visible = MacroRepair.VisibleInGame(text);
        return visible.Length > 0 ? visible : text;
    }

    /// <summary>A set the game has never written starts empty, with the version FFXI uses on a clean install.</summary>
    private MacroBook ReadOrCreate() => Info.Exists ? Info.Load() : new MacroBook { Version = 1 };
}
