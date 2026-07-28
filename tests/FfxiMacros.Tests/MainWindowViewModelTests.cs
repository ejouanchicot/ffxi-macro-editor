using FfxiMacros.App.ViewModels;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Settings;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// Drives the view models directly — no Avalonia, no window — so the editing rules are covered
/// by fast tests rather than by clicking.
/// </summary>
public class MainWindowViewModelTests : IDisposable
{
    private readonly TempUserFolder _temp = new();
    private readonly EditorSettings _settings;

    public MainWindowViewModelTests()
    {
        _temp.AddCharacter("a1b2c3d", 0, 1);
        _temp.AddTitles("a1b2c3d");
        _settings = new EditorSettings
        {
            UserFolder = _temp.UserFolder,
            BackupBeforeSave = false,
            BackupFolder = Path.Combine(_temp.Root, "Backups"),
        };
    }

    private MainWindowViewModel NewViewModel()
    {
        var viewModel = new MainWindowViewModel(_settings)
        {
            // No running client, whatever happens to be open on the machine running the tests.
            ProbeRunningClients = () => [],
        };
        viewModel.Initialize();
        return viewModel;
    }

    private static SetNodeViewModel FirstSet(MainWindowViewModel viewModel) =>
        viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1)
            .Sets.First(s => s.Info.SetNumber == 1);

    // ---------------------------------------------------------------- tree

    [Fact]
    public void Initialize_BuildsTheCharacterTree()
    {
        var viewModel = NewViewModel();

        var character = Assert.Single(viewModel.Characters.OfType<CharacterNodeViewModel>());
        Assert.Equal("a1b2c3d", character.Character.Id);
        Assert.Equal("ThfRdm", character.Books.First().Info.Title);
    }

    [Fact]
    public void AllFortyBooksAreListed_EmptyOnesIncluded()
    {
        // A character has forty book slots whatever it has written in them. They used to be hidden
        // behind a checkbox, which made an emptied book vanish and « put this on book 12 » a chore.
        _temp.AddCharacter("e5f6a7", 0);        // no title files: books 2-40 are untitled and empty
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First(c => c.Character.Id == "e5f6a7");

        Assert.Equal(40, character.Children.Count);
    }

    [Fact]
    public void ABookWithNoSetFileIsListedLikeAnyOther()
    {
        var character = NewViewModel().Characters.OfType<CharacterNodeViewModel>().First();

        Assert.Equal(40, character.Children.Count);
        Assert.False(character.Books.Last().Info.Exists);
    }

    [Fact]
    public void EachBookOffersItsTenSets()
    {
        var viewModel = NewViewModel();

        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();
        Assert.Equal(10, book.Sets.Count());
        Assert.True(book.Sets.First().Info.Exists);
        Assert.False(book.Sets.Last().Info.Exists);
    }

    // ---------------------------------------------------------------- opening

    [Fact]
    public void SelectingASetLoadsItsTwentyMacros()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);

        viewModel.CurrentSet = set;

        Assert.True(set.IsLoaded);
        Assert.Equal(20, set.Macros.Count);
        Assert.Equal("BuffSelf", set.Macros[0].Name);
        Assert.Equal("/con gs c smartbuff", set.Macros[0].Lines[0].Text);
        Assert.Equal("Ctrl-1", set.Macros[0].SlotLabel);
        Assert.Same(set, viewModel.CurrentSet);
    }

    [Fact]
    public void SelectingASetSelectsItsFirstNonEmptyMacro()
    {
        var viewModel = NewViewModel();

        viewModel.SelectedNode = FirstSet(viewModel);

        Assert.NotNull(viewModel.SelectedMacro);
        Assert.Equal("BuffSelf", viewModel.SelectedMacro.Name);
    }

    [Fact]
    public void SelectingABookOpensItsFirstSetThatExists()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();

        viewModel.SelectedNode = book;

        Assert.Same(book, viewModel.CurrentBook);
        Assert.Equal(10, viewModel.CurrentSets.Count);
        Assert.Same(book.Sets[0], viewModel.CurrentSet);
        Assert.True(viewModel.CurrentSet!.IsCurrent);
    }

    [Fact]
    public void OpeningAFolderLandsOnTheFirstBookThatHasMacros()
    {
        var viewModel = NewViewModel();

        Assert.NotNull(viewModel.CurrentBook);
        Assert.NotNull(viewModel.CurrentSet);
        Assert.True(viewModel.HasSelectedMacro);
    }

    [Fact]
    public void SetTabsAndMacroButtonsTrackTheCurrentSelection()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();
        viewModel.SelectedNode = book;

        viewModel.SelectSetCommand.Execute(book.Sets[3]);
        Assert.True(book.Sets[3].IsCurrent);
        Assert.False(book.Sets[0].IsCurrent);

        var slot = viewModel.CurrentSet!.Macros[7];
        viewModel.SelectMacroCommand.Execute(slot);
        Assert.True(slot.IsSelected);
        Assert.Single(viewModel.CurrentSet.Macros, m => m.IsSelected);
    }

    [Fact]
    public void TheSetColumnFollowsTheGameArrowOrderAroundSet1()
    {
        var viewModel = NewViewModel();
        viewModel.SelectedNode = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();

        // 7, 8, 9, 10, 1, 2, 3, 4, 5, 6 — up from set 1 reaches 10, down reaches 2.
        Assert.Equal(
            [7, 8, 9, 10, 1, 2, 3, 4, 5, 6],
            viewModel.CurrentSetWheel.Select(s => s.Info.SetNumber));

        // Set 1 is the anchor and the only one framed.
        Assert.Single(viewModel.CurrentSetWheel, s => s.IsHome);
        Assert.Equal(1, viewModel.CurrentSetWheel.Single(s => s.IsHome).Info.SetNumber);

        // Same ten sets as the plain ordering, just arranged differently.
        Assert.Equal(
            viewModel.CurrentSets.OrderBy(s => s.Info.SetNumber),
            viewModel.CurrentSetWheel.OrderBy(s => s.Info.SetNumber));
    }

    [Fact]
    public void PagingMovesThroughTheTenSetsAndStopsAtTheEnds()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();
        viewModel.SelectedNode = book;

        viewModel.NextSetCommand.Execute(null);
        Assert.Equal(2, viewModel.CurrentSet!.Info.SetNumber);

        viewModel.PreviousSetCommand.Execute(null);
        viewModel.PreviousSetCommand.Execute(null);
        Assert.Equal(1, viewModel.CurrentSet.Info.SetNumber);
    }

    // ---------------------------------------------------------------- editing

    [Fact]
    public void EditingALineMarksTheSetAndItsAncestorsDirty()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;

        set.Macros[0].Lines[1].Text = "/echo hello";

        Assert.True(set.IsDirty);
        Assert.True(set.Parent.IsDirty);
        Assert.True(set.Parent.Parent.IsDirty);
        Assert.Equal(1, viewModel.DirtyCount);
        Assert.Equal("1 set changed", viewModel.DirtySummary);
    }

    [Fact]
    public void SaveWritesTheFileAndClearsTheDirtyFlag()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;
        set.Macros[2].Name = "Renamed";

        Assert.True(viewModel.SaveCommand.CanExecute(null));
        viewModel.SaveCommand.Execute(null);

        Assert.False(set.IsDirty);
        Assert.Equal(0, viewModel.DirtyCount);
        Assert.Equal("Renamed", MacroBookFile.Load(set.Info.FullPath).Macros[2].Name);
        Assert.Equal(MacroBookFile.FileSize, new FileInfo(set.Info.FullPath).Length);
    }

    [Fact]
    public void SaveIsRefusedWhileTheSetHasNothingToSave()
    {
        var viewModel = NewViewModel();
        viewModel.SelectedNode = FirstSet(viewModel);

        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveIsBlockedWhileAFieldCannotBeEncoded()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;

        set.Macros[0].Name = "WayTooLongName";

        Assert.True(set.Macros[0].HasNameError);
        Assert.True(set.HasError);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void AnOverlongLineIsFlaggedWithItsByteCount()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;
        var line = set.Macros[0].Lines[0];

        line.Text = new string('a', 61);

        Assert.True(line.HasError);
        Assert.Equal(61, line.ByteCount);
        Assert.Equal("61/60", line.Counter);
        Assert.Contains("61 octets", line.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAutoTranslatePhraseCountsAsSixBytesNotThirteenCharacters()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;
        var line = set.Macros[0].Lines[0];

        line.Text = "/ja \"«02021F97»\" <t>";

        Assert.False(line.HasError);
        Assert.Equal(16, line.ByteCount);
    }

    [Fact]
    public void AMalformedEscapeIsReportedInsteadOfCrashing()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;
        var line = set.Macros[0].Lines[0];

        line.Text = "/echo {NOPE}";

        Assert.True(line.HasError);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void ClearEmptiesEveryFieldOfAMacro()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;
        var macro = set.Macros[0];

        macro.ClearCommand.Execute(null);

        Assert.True(macro.IsEmpty);
        Assert.Equal("", macro.Name);
        Assert.Equal("", macro.Lines[0].Text);
        Assert.Equal("—", macro.DisplayName);
        Assert.True(set.IsDirty);
    }

    [Fact]
    public void ReloadThrowsAwayUnsavedEdits()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;
        set.Macros[0].Name = "Draft";

        viewModel.ReloadCommand.Execute(null);

        Assert.Equal("BuffSelf", set.Macros[0].Name);
        Assert.False(set.IsDirty);
        Assert.Equal(0, viewModel.DirtyCount);
    }

    [Fact]
    public void SaveAllWritesEverySetThatChanged()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();
        var set1 = book.Sets[0];
        var set2 = book.Sets[1];

        viewModel.CurrentSet = set1;
        set1.Macros[0].Name = "One";
        viewModel.CurrentSet = set2;
        set2.Macros[0].Name = "Two";

        Assert.Equal(2, viewModel.DirtyCount);
        viewModel.SaveAllCommand.Execute(null);

        Assert.Equal(0, viewModel.DirtyCount);
        Assert.Equal("One", MacroBookFile.Load(set1.Info.FullPath).Macros[0].Name);
        Assert.Equal("Two", MacroBookFile.Load(set2.Info.FullPath).Macros[0].Name);
    }

    [Fact]
    public void SaveAllSkipsTheSetsThatCannotBeEncodedAndSaysSo()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();
        var good = book.Sets[0];
        var broken = book.Sets[1];

        viewModel.CurrentSet = good;
        good.Macros[0].Name = "Good";
        viewModel.CurrentSet = broken;
        broken.Macros[0].Name = "ThisNameIsFarTooLong";

        viewModel.SaveAllCommand.Execute(null);

        Assert.Equal("Good", MacroBookFile.Load(good.Info.FullPath).Macros[0].Name);
        Assert.True(broken.IsDirty);
        Assert.True(viewModel.StatusIsError);
        Assert.Contains("blocked", viewModel.Status, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- new sets and backups

    [Fact]
    public void ASetThatDoesNotExistYetOpensEmptyAndIsCreatedOnSave()
    {
        var viewModel = NewViewModel();
        var set = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First().Sets[^1];
        Assert.False(set.Info.Exists);

        viewModel.CurrentSet = set;
        set.Macros[0].Name = "New";
        set.Macros[0].Lines[0].Text = "/echo new set";
        viewModel.SaveCommand.Execute(null);

        Assert.True(File.Exists(set.Info.FullPath));
        Assert.True(set.Info.Exists);
        Assert.Equal(MacroBookFile.FileSize, new FileInfo(set.Info.FullPath).Length);
        Assert.Equal("/echo new set", MacroBookFile.Load(set.Info.FullPath).Macros[0].Lines[0]);
    }

    [Fact]
    public void TheCharacterIsBackedUpOnceBeforeTheFirstWrite()
    {
        _settings.BackupBeforeSave = true;
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();

        viewModel.CurrentSet = book.Sets[0];
        book.Sets[0].Macros[0].Name = "One";
        viewModel.SaveCommand.Execute(null);

        viewModel.CurrentSet = book.Sets[1];
        book.Sets[1].Macros[0].Name = "Two";
        viewModel.SaveCommand.Execute(null);

        string backupRoot = Path.Combine(_temp.Root, "Backups");
        string backup = Assert.Single(Directory.EnumerateDirectories(backupRoot));
        Assert.StartsWith("a1b2c3d-", Path.GetFileName(backup), StringComparison.Ordinal);
        Assert.Contains("mcr.dat", Directory.EnumerateFiles(backup).Select(Path.GetFileName));
    }

    [Fact]
    public void RefreshIsRefusedWhileEditsAreUnsaved()
    {
        var viewModel = NewViewModel();
        var set = FirstSet(viewModel);
        viewModel.CurrentSet = set;
        set.Macros[0].Name = "Draft";

        viewModel.RefreshCommand.Execute(null);

        Assert.True(viewModel.StatusIsError);
        Assert.Equal(1, viewModel.DirtyCount);
        Assert.Equal("Draft", set.Macros[0].Name);
    }

    // ---------------------------------------------------------------- titles

    [Fact]
    public void RenamingABookWritesTheTitleFile()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First().Books.First();

        book.Rename("ThfWhm");

        // The number lives in the chip beside the name, so the header is the title alone.
        Assert.Equal("ThfWhm", book.Header);
        Assert.Equal("1", book.Badge);
        Assert.Equal("ThfWhm", BookTitleSet.Load(Path.Combine(_temp.UserFolder, "a1b2c3d", "mcr.ttl")).Titles[0]);
    }

    public void Dispose() => _temp.Dispose();
}
