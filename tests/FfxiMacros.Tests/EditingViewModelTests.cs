using FfxiMacros.App.ViewModels;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Serialization;
using FfxiMacros.Core.Settings;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>Covers the milestone 4 editing features through the view models.</summary>
public class EditingViewModelTests : IDisposable
{
    private readonly TempUserFolder _temp = new();
    private readonly EditorSettings _settings;

    public EditingViewModelTests()
    {
        _temp.AddCharacter("aaaa1", 0, 1);
        _temp.AddTitles("aaaa1");
        _settings = new EditorSettings
        {
            UserFolder = _temp.UserFolder,
            BackupBeforeSave = false,
            BackupFolder = Path.Combine(_temp.Root, "Backups"),
        };
    }

    private MainWindowViewModel NewViewModel(params string[] runningClients)
    {
        var viewModel = new MainWindowViewModel(_settings)
        {
            // Tests must not depend on whether FFXI happens to be running on this machine.
            ProbeRunningClients = () => runningClients,
        };
        viewModel.Initialize();
        return viewModel;
    }

    private static SetNodeViewModel CurrentSet(MainWindowViewModel viewModel) => viewModel.CurrentSet!;

    // ---------------------------------------------------------------- clipboard and drag/drop

    [Fact]
    public void CopyThenPaste_DuplicatesAMacro()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);

        viewModel.CopyMacroToClipboard(set.Macros[0]);
        viewModel.PasteMacroFromClipboard(set.Macros[15]);

        Assert.True(viewModel.CanPasteMacro);
        Assert.Equal("BuffSelf", set.Macros[15].Name);
        Assert.Equal("BuffSelf", set.Macros[0].Name);   // the source is untouched
        Assert.True(set.IsDirty);
    }

    [Fact]
    public void PasteWithNothingCopiedDoesNothing()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        string before = set.Macros[15].Name;

        viewModel.PasteMacroFromClipboard(set.Macros[15]);

        Assert.False(viewModel.CanPasteMacro);
        Assert.Equal(before, set.Macros[15].Name);
        Assert.False(set.IsDirty);
    }

    [Fact]
    public void DraggingAMacroOntoAnotherSwapsThem()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        string first = set.Macros[0].Name;
        string second = set.Macros[1].Name;

        viewModel.TransferMacro(set.Macros[0], set.Macros[1], copy: false);

        Assert.Equal(second, set.Macros[0].Name);
        Assert.Equal(first, set.Macros[1].Name);
        Assert.Same(set.Macros[1], viewModel.SelectedMacro);
    }

    [Fact]
    public void DraggingWithCopyLeavesTheSourceInPlace()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);

        viewModel.TransferMacro(set.Macros[0], set.Macros[1], copy: true);

        Assert.Equal("BuffSelf", set.Macros[0].Name);
        Assert.Equal("BuffSelf", set.Macros[1].Name);
    }

    [Fact]
    public void DraggingAMacroOntoItselfChangesNothing()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);

        viewModel.TransferMacro(set.Macros[0], set.Macros[0], copy: false);

        Assert.Equal("BuffSelf", set.Macros[0].Name);
        Assert.False(set.IsDirty);
    }

    [Fact]
    public void AMacroCanBeMovedBetweenTwoSetsOfTheSameBook()
    {
        var viewModel = NewViewModel();
        var book = viewModel.CurrentBook!;
        var first = book.Sets[0];
        var second = book.Sets[1];
        viewModel.CurrentSet = second;      // load it
        viewModel.CurrentSet = first;

        viewModel.CopyMacroToClipboard(first.Macros[0]);
        viewModel.CurrentSet = second;
        viewModel.PasteMacroFromClipboard(second.Macros[19]);

        Assert.Equal("BuffSelf", second.Macros[19].Name);
        Assert.True(second.IsDirty);
    }

    // ---------------------------------------------------------------- repair

    [Fact]
    public void Repair_FixesTheBrokenLinesOfTheCurrentSet()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        var line = set.Macros[6].Lines[1];

        // The stored bytes open with a NUL, so the game runs nothing: the field reads empty and the
        // recoverable text is reported beside it.
        Assert.Equal("", line.Text);
        Assert.True(line.HasHiddenBytes);
        Assert.Equal("{00}con send Kaelith \"Healing Waltz\" <laststid>", line.HiddenText);

        viewModel.RepairCommand.Execute(null);

        Assert.Equal("/con send Kaelith \"Healing Waltz\" <laststid>", line.Text);
        Assert.False(line.HasHiddenBytes);
        Assert.True(set.IsDirty);
        Assert.Contains("repaired", viewModel.Status, StringComparison.Ordinal);
    }

    /// <summary>The macro the user reported: Ctrl-8 of PldRunR, whose first line ends in dead bytes.</summary>
    private MacroSlotViewModel DamagedCureIii(MainWindowViewModel viewModel)
    {
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "pldrun");
        viewModel.SelectedNode = character.Books.First(b => b.Info.Number == 1);
        return viewModel.CurrentSet!.Macros[7];
    }

    [Fact]
    public void ALineIsShownAsTheGameRunsIt()
    {
        // The file holds /ta <stpc>, a NUL, then the tail of an older, longer line.
        _temp.AddCharacterFrom("pldrun", "mcr140.dat", 0);
        var line = DamagedCureIii(NewViewModel()).Lines[0];

        Assert.Equal("/ta <stpc>", line.Text);
        Assert.True(line.HasHiddenBytes);
        Assert.Contains("Le jeu ignore la suite", line.HiddenWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void CleaningOneLineDropsItsDeadBytesAndNothingElse()
    {
        _temp.AddCharacterFrom("pldrun", "mcr140.dat", 0);
        var viewModel = NewViewModel();
        var macro = DamagedCureIii(viewModel);
        string secondLine = macro.Lines[1].Text;

        macro.Lines[0].RepairCommand.Execute(null);

        Assert.Equal("/ta <stpc>", macro.Lines[0].Text);
        Assert.False(macro.Lines[0].HasHiddenBytes);
        Assert.Equal(secondLine, macro.Lines[1].Text);
        Assert.True(viewModel.CurrentSet!.IsDirty);
    }

    [Fact]
    public void ADamagedNameReadsAsTheGameShowsIt()
    {
        var macro = CurrentSet(NewViewModel()).Macros[6];

        Assert.Equal("SA", macro.Name);            // stored as SA{00}se
        Assert.True(macro.HasHiddenName);
    }

    [Fact]
    public void SavingWritesWhatTheEditorShowsAndDropsTheDeadBytes()
    {
        _temp.AddCharacterFrom("pldrun", "mcr140.dat", 0);
        var viewModel = NewViewModel();
        var macro = DamagedCureIii(viewModel);
        var set = viewModel.CurrentSet!;
        Assert.True(macro.Lines[0].HasHiddenBytes);

        macro.Lines[2].Text = "/echo ok";           // any edit, so there is something to save
        viewModel.SaveCommand.Execute(null);

        var reloaded = MacroBookFile.Load(set.Info.FullPath);
        Assert.Equal("/ta <stpc>", reloaded.Macros[7].Lines[0]);
        Assert.DoesNotContain("{00}", reloaded.Macros[7].Lines[0], StringComparison.Ordinal);
        Assert.Equal(MacroBookFile.FileSize, new FileInfo(set.Info.FullPath).Length);
    }

    [Fact]
    public void SavingKeepsAFieldThatIsEntirelyDeadSoItCanStillBeRecovered()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        Assert.Equal("", set.Macros[6].Lines[1].Text);   // stored as {00}con send …

        set.Macros[0].Name = "Edit";
        viewModel.SaveCommand.Execute(null);

        // Nothing visible to write, so the recoverable text stays for « Réparer » to restore.
        var reloaded = MacroBookFile.Load(set.Info.FullPath);
        Assert.StartsWith("{00}con send", reloaded.Macros[6].Lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void BrowsingMacrosAndSetsNeverMarksAnythingModified()
    {
        _temp.AddCharacterFrom("pldrun", "mcr140.dat", 0, 1);
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "pldrun");

        viewModel.SelectedNode = character.Books.First(b => b.Info.Number == 1);
        foreach (var slot in viewModel.CurrentSet!.Macros)
            viewModel.SelectMacroCommand.Execute(slot);

        viewModel.SelectSetCommand.Execute(viewModel.CurrentSets[1]);
        foreach (var slot in viewModel.CurrentSet!.Macros)
            viewModel.SelectMacroCommand.Execute(slot);

        Assert.Equal(0, viewModel.DirtyCount);
        Assert.Equal("No changes", viewModel.DirtySummary);
    }

    [Fact]
    public void MovingThroughADamagedLineWithoutTypingChangesNothing()
    {
        var viewModel = NewViewModel();
        var line = CurrentSet(viewModel).Macros[6].Lines[1];

        line.Text = line.Text;                     // what a focus/blur round trip sends back

        Assert.True(line.HasHiddenBytes);
        Assert.False(CurrentSet(viewModel).IsDirty);
    }

    [Fact]
    public void Repair_SaysSoWhenThereIsNothingToFix()
    {
        var viewModel = NewViewModel();
        viewModel.RepairCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);

        viewModel.RepairCommand.Execute(null);

        Assert.Contains("nothing to repair", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepairedSetSavesAndReloadsClean()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);

        viewModel.RepairCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);

        var reloaded = MacroBookFile.Load(set.Info.FullPath);
        Assert.Equal("/con send Kaelith \"Healing Waltz\" <laststid>", reloaded.Macros[6].Lines[1]);
        Assert.Equal(MacroBookFile.FileSize, new FileInfo(set.Info.FullPath).Length);
    }

    // ---------------------------------------------------------------- import / export

    [Fact]
    public async Task ExportThenImport_BringsTheMacrosBack()
    {
        string path = Path.Combine(_temp.Root, "export.txt");
        var viewModel = NewViewModel();
        viewModel.SaveFileAsync = (_, _) => Task.FromResult<string?>(path);
        viewModel.OpenFileAsync = _ => Task.FromResult<string?>(path);

        viewModel.ExportSetCommand.Execute(null);
        await WaitForFile(path);

        var set = CurrentSet(viewModel);
        set.Macros[0].ClearCommand.Execute(null);
        Assert.True(set.Macros[0].IsEmpty);

        viewModel.ImportSetCommand.Execute(null);
        await WaitUntil(() => !set.Macros[0].IsEmpty);

        Assert.Equal("BuffSelf", set.Macros[0].Name);
        Assert.Equal("/con gs c smartbuff", set.Macros[0].Lines[0].Text);
    }

    [Fact]
    public async Task ExportingToJsonProducesAReadableDocument()
    {
        string path = Path.Combine(_temp.Root, "export.json");
        var viewModel = NewViewModel();
        viewModel.SaveFileAsync = (_, _) => Task.FromResult<string?>(path);

        viewModel.ExportSetCommand.Execute(null);
        await WaitForFile(path);

        var document = MacroJsonFormat.Parse(File.ReadAllText(path));
        Assert.Equal("aaaa1", document.Character);
        Assert.Equal(1, document.Book);
        Assert.Equal("ThfRdm", document.Title);
        Assert.NotEmpty(document.Sets[0].Macros);
    }

    [Fact]
    public async Task ImportingRubbishIsReportedInsteadOfCrashing()
    {
        string path = Path.Combine(_temp.Root, "bad.txt");
        File.WriteAllText(path, "/echo orphan line with no header");
        var viewModel = NewViewModel();
        viewModel.OpenFileAsync = _ => Task.FromResult<string?>(path);

        viewModel.ImportSetCommand.Execute(null);
        await WaitUntil(() => viewModel.StatusIsError);

        Assert.True(viewModel.StatusIsError);
        Assert.False(CurrentSet(viewModel).IsDirty);
    }

    // ---------------------------------------------------------------- book copy / move

    [Fact]
    public void DroppingABookOnlyProposesTheOperation()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        var source = character.Books.First(b => b.Info.Number == 1);
        var target = character.Books.First(b => b.Info.Number == 9);

        viewModel.RequestBookTransfer(source, target, move: false);

        Assert.True(viewModel.HasPendingBookOperation);
        Assert.Equal(0, target.Info.SetCount);       // nothing written yet
        Assert.Contains("Copy book 1", viewModel.PendingBookOperation!.Question, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmingABookCopyWritesTheFilesAndTheTitle()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        viewModel.RequestBookTransfer(
            character.Books.First(b => b.Info.Number == 1),
            character.Books.First(b => b.Info.Number == 9),
            move: false);

        viewModel.ConfirmBookOperationCommand.Execute(null);

        Assert.False(viewModel.HasPendingBookOperation);
        var reloaded = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 9);
        Assert.Equal(2, reloaded.Info.SetCount);
        Assert.Equal("ThfRdm", reloaded.Info.Title);
    }

    [Fact]
    public void CancellingABookOperationLeavesTheDiskAlone()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        var target = character.Books.First(b => b.Info.Number == 9);
        viewModel.RequestBookTransfer(character.Books.First(b => b.Info.Number == 1), target, move: false);

        viewModel.CancelBookOperationCommand.Execute(null);

        Assert.False(viewModel.HasPendingBookOperation);
        Assert.Equal(0, target.Info.SetCount);
    }

    [Fact]
    public void ABookOperationIsRefusedWhileEditsAreUnsaved()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        CurrentSet(viewModel).Macros[0].Name = "Draft";

        viewModel.RequestBookTransfer(
            character.Books.First(b => b.Info.Number == 1),
            character.Books.First(b => b.Info.Number == 9),
            move: false);

        Assert.False(viewModel.HasPendingBookOperation);
        Assert.True(viewModel.StatusIsError);
    }

    // ---------------------------------------------------------------- the running game

    [Fact]
    public void SavingGoesThroughWhileLoggedIn_BecauseOnlyTheBookOnScreenIsAtRisk()
    {
        // The client reads a book from disk when you switch to it, so editing any other book works.
        var viewModel = NewViewModel("Kaelith");
        var set = CurrentSet(viewModel);
        set.Macros[0].Name = "Edited";

        viewModel.SaveCommand.Execute(null);

        Assert.False(set.IsDirty);
        Assert.Equal("Edited", MacroBookFile.Load(set.Info.FullPath).Macros[0].Name);
        Assert.Contains("Switch to book", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBannerExplainsWhichBookIsActuallyAtRisk()
    {
        var viewModel = NewViewModel("Kaelith");

        Assert.True(viewModel.ShowGameRunningBanner);
        Assert.Contains("only holds the book shown on screen", viewModel.GameRunningWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWarningNamesEveryClientThatIsRunning()
    {
        var viewModel = NewViewModel("Kaelith", "Sylvane");

        Assert.True(viewModel.ShowGameRunningBanner);
        Assert.Contains("Kaelith, Sylvane", viewModel.GameRunningWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggingOutClearsTheBanner()
    {
        string[] running = ["Kaelith"];
        var viewModel = new MainWindowViewModel(_settings) { ProbeRunningClients = () => running };
        viewModel.Initialize();
        Assert.True(viewModel.ShowGameRunningBanner);

        running = [];
        viewModel.RecheckGameCommand.Execute(null);

        Assert.False(viewModel.IsGameRunning);
        Assert.False(viewModel.ShowGameRunningBanner);
        Assert.Contains("Nobody is in game", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void NoAdviceIsAddedWhenNobodyIsLoggedIn()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        set.Macros[0].Name = "Edited";

        viewModel.SaveCommand.Execute(null);

        Assert.DoesNotContain("Bascule", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ABookOperationGoesThroughToo()
    {
        var viewModel = NewViewModel("Kaelith");
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        var target = character.Books.First(b => b.Info.Number == 9);
        viewModel.RequestBookTransfer(character.Books.First(b => b.Info.Number == 1), target, move: false);

        viewModel.ConfirmBookOperationCommand.Execute(null);

        Assert.False(viewModel.HasPendingBookOperation);
        Assert.Equal(2, viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 9).Info.SetCount);
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public void Search_ListsHitsAndOpensTheOneSelected()
    {
        var viewModel = NewViewModel();
        viewModel.SearchQuery = "Provoke";

        viewModel.SearchCommand.Execute(null);

        Assert.True(viewModel.SearchPanelOpen);
        Assert.NotEmpty(viewModel.SearchResults);

        viewModel.SelectedSearchResult = viewModel.SearchResults[0];
        var hit = viewModel.SelectedSearchResult.Hit;
        Assert.Equal(hit.BookNumber, viewModel.CurrentBook!.Info.Number);
        Assert.Equal(hit.SetNumber, viewModel.CurrentSet!.Info.SetNumber);
        Assert.Equal(hit.MacroIndex, viewModel.SelectedMacro!.Index);
    }

    [Fact]
    public void Search_WithNoHitSaysSo()
    {
        var viewModel = NewViewModel();
        viewModel.SearchQuery = "zzz-introuvable-zzz";

        viewModel.SearchCommand.Execute(null);

        Assert.Empty(viewModel.SearchResults);
        Assert.Contains("No result", viewModel.SearchSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_OfAnEmptyQueryClearsTheResults()
    {
        var viewModel = NewViewModel();
        viewModel.SearchQuery = "Provoke";
        viewModel.SearchCommand.Execute(null);

        viewModel.SearchQuery = "   ";
        viewModel.SearchCommand.Execute(null);

        Assert.Empty(viewModel.SearchResults);
    }

    private static async Task WaitForFile(string path) => await WaitUntil(() => File.Exists(path));

    /// <summary>The async commands are fire-and-forget, so tests wait for the effect.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);

        Assert.True(condition(), "The expected effect never happened.");
    }

    public void Dispose() => _temp.Dispose();
}
