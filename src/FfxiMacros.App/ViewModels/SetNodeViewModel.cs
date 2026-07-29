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
    private int? _usedMacros;

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

    /// <summary>
    /// How many of the 20 slots hold a macro. Read straight from the file when the set has not been
    /// opened yet, and cached: it is what the tab is dimmed by, so it is asked for on every redraw.
    /// </summary>
    public int UsedMacros
    {
        get
        {
            if (Loaded is not null)
                return Loaded.Macros.Count(m => !m.IsEmpty);

            if (_usedMacros is null)
            {
                try
                {
                    _usedMacros = Info.Exists ? Info.Load().Macros.Count(m => !m.IsEmpty) : 0;
                }
                catch (MacroFileException)
                {
                    _usedMacros = 0;   // a file that will not load is reported when it is opened
                }
            }

            return _usedMacros.Value;
        }
    }

    /// <summary>True for a set holding nothing, whether or not the game ever wrote its file.</summary>
    public bool IsEmptySet => UsedMacros == 0;

    /// <summary>
    /// Dims the tab of a set with nothing in it.
    /// </summary>
    /// <remarks>
    /// It used to follow the file's existence, which told the player something about the disk rather
    /// than about their macros: a set the game had written and then emptied looked as full as one
    /// carrying twenty macros.
    /// </remarks>
    public double TabOpacity => IsEmptySet ? 0.4 : 1.0;

    /// <summary>
    /// True for set 1, the set players anchor on: the game's up arrow from there reaches set 10 and
    /// the down arrow set 2, so it gets a frame of its own in the set column.
    /// </summary>
    public bool IsHome => Info.SetNumber == 1;

    // The highlight on the set tab is the node's own IsCurrent: a set being edited and a book being
    // edited are the same idea, and were the same property written twice.

    public override bool IsDirty => _isDirty;

    public bool IsLoaded => Loaded is not null;

    /// <summary>True when a macro holds text that cannot be written back; blocks saving.</summary>
    public bool HasError => Macros.Any(m => m.HasError);

    /// <summary>
    /// When the file was last written, as it stood the moment this set was read.
    /// </summary>
    /// <remarks>
    /// Compared against the file on disk to notice the game rewriting a set behind the editor's
    /// back — which it does to the book it holds, whenever it switches away from it.
    /// </remarks>
    private DateTime _readAtWriteUtc;

    /// <summary>True when the file changed on disk since it was read here.</summary>
    public bool ChangedOnDisk => IsLoaded && Info.LastWriteUtc != _readAtWriteUtc;

    /// <summary>Re-reads what the file system says about the file, leaving the macros alone.</summary>
    internal void RefreshFromDisk()
    {
        Info.Refresh();
        RefreshFill();
        RefreshLabels();
    }

    /// <summary>Reads the file, or starts from an empty set when the file does not exist yet.</summary>
    public void Load()
    {
        Loaded = ReadOrCreate();
        _readAtWriteUtc = Info.LastWriteUtc;
        Macros.Clear();
        for (int i = 0; i < MacroBook.MacroCount; i++)
            Macros.Add(new MacroSlotViewModel(this, i));

        _isDirty = false;
        RefreshLabels();
        RefreshFill();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>Re-asks how full the set is, after anything that could have changed it.</summary>
    private void RefreshFill()
    {
        _usedMacros = null;
        OnPropertyChanged(nameof(UsedMacros));
        OnPropertyChanged(nameof(IsEmptySet));
        OnPropertyChanged(nameof(TabOpacity));
    }

    /// <summary>Throws away unsaved edits and re-reads the file.</summary>
    public void Reload()
    {
        if (!IsLoaded)
            return;

        Info.Refresh();
        Loaded = ReadOrCreate();
        _readAtWriteUtc = Info.LastWriteUtc;
        foreach (var slot in Macros)
            slot.NotifyMacroReplaced();

        _isDirty = false;
        RefreshLabels();
        RefreshFill();
        Parent.RefreshUpwards();
    }

    public void Save()
    {
        if (Loaded is null)
            return;

        DropDeadBytes();
        Info.Save(Loaded);   // refreshes the file's size and timestamp
        _readAtWriteUtc = Info.LastWriteUtc;

        _isDirty = false;
        RefreshLabels();
        RefreshFill();
        Parent.RefreshUpwards();
    }

    /// <summary>Marks the set as holding unsaved edits.</summary>
    public void MarkDirty()
    {
        _isDirty = true;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasError));
        RefreshLabels();
        RefreshFill();
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
