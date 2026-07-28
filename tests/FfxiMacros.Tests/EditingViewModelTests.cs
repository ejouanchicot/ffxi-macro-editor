using FfxiMacros.App.ViewModels;
using FfxiMacros.Core.Discovery;
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

            // Anything the view model saves has to land here. Without it, a test that renames a
            // character writes into the real settings file of whoever is running the suite.
            SourcePath = Path.Combine(_temp.Root, "settings.json"),
        };
    }

    private MainWindowViewModel NewViewModel(params string[] runningClients)
    {
        var viewModel = new MainWindowViewModel(_settings)
        {
            // Tests must not depend on whether FFXI happens to be running on this machine.
            ProbeRunningClients = () => runningClients,

            // Nor on what a Windower addon may have written into the real application folder.
            LiveStateFolder = Path.Combine(_temp.Root, "live"),
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

    [Fact]
    public void CopyingASetReplacesTheTwentyMacrosOfAnotherSet()
    {
        var viewModel = NewViewModel();
        var book = viewModel.CurrentBook!;
        var source = book.Sets[0];
        var target = book.Sets[1];

        viewModel.CurrentSet = target;              // load it, then make it differ from the source
        target.Macros[0].Name = "Ours";
        viewModel.CurrentSet = source;

        viewModel.CopySetToClipboard(source);
        viewModel.PasteSetFromClipboard(target);

        Assert.True(viewModel.CanPasteSet);
        Assert.Equal(ClipboardKind.Set, viewModel.Clipboard);
        Assert.Equal("BuffSelf", target.Macros[0].Name);
        Assert.True(target.IsDirty);
    }

    [Fact]
    public void APastedSetIsACopy_SoEditingTheSourceLeavesItAlone()
    {
        var viewModel = NewViewModel();
        var book = viewModel.CurrentBook!;
        var source = book.Sets[0];
        var target = book.Sets[1];

        viewModel.CopySetToClipboard(source);
        viewModel.PasteSetFromClipboard(target);
        source.Macros[0].Name = "Later";

        Assert.Equal("BuffSelf", target.Macros[0].Name);
    }

    [Fact]
    public void PastingASetWithNothingCopiedSaysSoAndChangesNothing()
    {
        var viewModel = NewViewModel();
        var target = viewModel.CurrentSets[1];

        viewModel.PasteSetFromClipboard(target);

        Assert.False(viewModel.CanPasteSet);
        Assert.True(viewModel.StatusIsError);
        Assert.False(target.IsDirty);
    }

    [Fact]
    public void CopyingABookAndPastingItStillGoesThroughTheConfirmation()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        var target = character.Books.First(b => b.Info.Number == 9);

        viewModel.CopyBookToClipboard(character.Books.First(b => b.Info.Number == 1));
        viewModel.PasteBookFromClipboard(target);

        Assert.Equal(ClipboardKind.Book, viewModel.Clipboard);
        Assert.True(viewModel.HasPendingBookOperation);
        Assert.Equal(0, target.Info.SetCount);      // nothing written until it is confirmed

        viewModel.ConfirmBookOperationCommand.Execute(null);

        Assert.Equal(2, viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 9).Info.SetCount);
    }

    [Fact]
    public void ABookStaysOnTheClipboardAfterACopyRebuiltTheTree()
    {
        // Confirming a book copy re-reads the whole folder, which replaces every node: pasting the
        // same book a second time has to find the book as it is now, not the node that was copied.
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();

        viewModel.CopyBookToClipboard(character.Books.First(b => b.Info.Number == 1));
        viewModel.PasteBookFromClipboard(character.Books.First(b => b.Info.Number == 9));
        viewModel.ConfirmBookOperationCommand.Execute(null);

        var rebuilt = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        viewModel.PasteBookFromClipboard(rebuilt.Books.First(b => b.Info.Number == 10));
        viewModel.ConfirmBookOperationCommand.Execute(null);

        var book10 = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 10);
        Assert.Equal(2, book10.Info.SetCount);
        Assert.Equal("ThfRdm", book10.Info.Title);
    }

    [Fact]
    public void EmptyingASetClearsItsTwentyMacros_AndReloadBringsThemBack()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);

        viewModel.ClearSet(set);

        Assert.All(set.Macros, macro => Assert.True(macro.IsEmpty));
        Assert.True(set.IsDirty);

        viewModel.ReloadCommand.Execute(null);

        Assert.Equal("BuffSelf", set.Macros[0].Name);
        Assert.False(set.IsDirty);
    }

    [Fact]
    public void EmptyingAnAlreadyEmptySetSaysSoRatherThanMarkingItChanged()
    {
        var viewModel = NewViewModel();
        var set = viewModel.CurrentSets[5];       // never written by the game
        viewModel.CurrentSet = set;

        viewModel.ClearSet(set);

        Assert.False(set.IsDirty);
        Assert.Contains("already empty", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyingABookDeletesItsSetFilesAndResetsItsTitle()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1);
        Assert.Equal(2, book.Info.SetCount);

        viewModel.RequestBookClear(book);

        Assert.True(viewModel.HasPendingBookOperation);
        Assert.Contains("Empty book 1", viewModel.PendingBookOperation!.Question, StringComparison.Ordinal);
        Assert.Equal(2, book.Info.SetCount);      // nothing deleted until it is confirmed

        viewModel.ConfirmBookOperationCommand.Execute(null);

        var emptied = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1);
        Assert.Equal(0, emptied.Info.SetCount);
        Assert.True(emptied.IsEmptyAndUntitled);
    }

    [Fact]
    public void EmptyingAnAlreadyEmptyBookIsNotEvenProposed()
    {
        // A character with no title file: its books carry the game's BookNN placeholder, so book 2
        // holds nothing at all — no set file and no name to reset.
        _temp.AddCharacter("bbbb2", 0);
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "bbbb2")
            .Books.First(b => b.Info.Number == 2);
        Assert.True(book.IsEmptyAndUntitled);

        viewModel.RequestBookClear(book);

        Assert.False(viewModel.HasPendingBookOperation);
        Assert.Contains("already empty", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptiedBookKeepsItsRowInTheList()
    {
        // The slot still exists on the character — watching the row vanish reads as « book deleted ».
        var viewModel = NewViewModel();

        viewModel.RequestBookClear(viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1));
        viewModel.ConfirmBookOperationCommand.Execute(null);

        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        var listed = character.Children.OfType<BookNodeViewModel>().First(b => b.Info.Number == 1);
        Assert.True(listed.IsEmptyAndUntitled);
    }

    [Fact]
    public void TheSourceOfAMovedBookAlsoKeepsItsRow()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();

        viewModel.RequestBookTransfer(
            character.Books.First(b => b.Info.Number == 1),
            character.Books.First(b => b.Info.Number == 9),
            move: true);
        viewModel.ConfirmBookOperationCommand.Execute(null);

        var rebuilt = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        Assert.Contains(rebuilt.Children.OfType<BookNodeViewModel>(), b => b.Info.Number == 1);
    }

    // ---------------------------------------------------------------- renaming a book

    [Fact]
    public void RenamingABookWritesTheTitleTheGameReads()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1);

        viewModel.BeginRename(book);
        Assert.True(book.IsRenaming);
        Assert.Equal("ThfRdm", book.RenameDraft);

        book.RenameDraft = "Nin";
        viewModel.CommitRename(book);

        Assert.False(book.IsRenaming);
        Assert.Equal("Nin", book.Info.Title);

        // Straight from the title file, the way the client reads it.
        var titles = FfxiMacros.Core.Io.BookTitleSet.Load(
            Path.Combine(_temp.UserFolder, "aaaa1", "mcr.ttl"));
        Assert.Equal("Nin", titles.Titles[0]);      // index 0 of the first half is book 1
    }

    [Fact]
    public void ATitleThatDoesNotFitIsRefusedAndTheRowStaysOpen()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1);

        viewModel.BeginRename(book);
        book.RenameDraft = "BeaucoupTropLongPourUnTitreDeBook";
        viewModel.CommitRename(book);

        Assert.True(viewModel.StatusIsError);
        Assert.True(book.IsRenaming);          // still there to be shortened
        Assert.Equal("ThfRdm", book.Info.Title);
    }

    [Fact]
    public void CancellingARenameLeavesTheTitleAlone()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1);

        viewModel.BeginRename(book);
        book.RenameDraft = "Autre";
        viewModel.CancelRename(book);

        Assert.False(book.IsRenaming);
        Assert.Equal("ThfRdm", book.Info.Title);
    }

    [Fact]
    public void CancellingAnEmptyingLeavesTheFilesAlone()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 1);

        viewModel.RequestBookClear(book);
        viewModel.CancelBookOperationCommand.Execute(null);

        Assert.False(viewModel.HasPendingBookOperation);
        Assert.Equal(2, book.Info.SetCount);
    }

    [Fact]
    public void TheThreeClipboardsAreKeptApart()
    {
        // Copying a book must not throw away the macro that was copied before it: Ctrl+V is answered
        // by whichever clipboard matches what the user aimed at.
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);

        viewModel.CopyToClipboard(set.Macros[0]);
        viewModel.CopyToClipboard(viewModel.CurrentBook!);
        viewModel.PasteFromClipboard(set.Macros[19]);

        Assert.True(viewModel.CanPasteMacro);
        Assert.True(viewModel.CanPasteBook);
        Assert.False(viewModel.CanPasteSet);
        Assert.Equal("BuffSelf", set.Macros[19].Name);
    }

    // ---------------------------------------------------------------- what the tree and tabs show

    [Fact]
    public void ASetIsDimmedOnceItHoldsNothing_FileOrNoFile()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        Assert.False(set.IsEmptySet);
        Assert.Equal(1.0, set.TabOpacity);

        viewModel.ClearSet(set);

        // Emptied by hand: the file is still there, the tab is not pretending it holds macros.
        Assert.True(set.IsEmptySet);
        Assert.True(set.TabOpacity < 1.0);
        Assert.True(viewModel.CurrentSets[5].IsEmptySet);      // never written by the game
    }

    [Fact]
    public void AnUnopenedSetReportsHowFullItIsWithoutBeingOpened()
    {
        var viewModel = NewViewModel();
        var set = viewModel.CurrentSets[1];
        Assert.False(set.IsLoaded);

        Assert.True(set.UsedMacros > 0);
        Assert.False(set.IsLoaded);                            // read straight from the file
    }

    [Fact]
    public void TheBookMarkedIsTheOneTheGameRecorded()
    {
        _temp.AddCharacter("aaaa1", 20);                    // book 3, set 1
        _temp.SetCurrentSet("aaaa1", 20);                   // mcr.sys: the game is on book 3
        _temp.Touch("aaaa1", 20, DateTime.UtcNow.AddHours(-1));

        var books = NewViewModel().Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();

        Assert.True(books.First(b => b.Info.Number == 3).IsOpenInGame);
        Assert.False(books.First(b => b.Info.Number == 1).IsOpenInGame);
        Assert.Contains("where the game left this character", books.First(b => b.Info.Number == 3).Detail,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void WhatTheClientHoldsInMemoryBeatsEverythingElse()
    {
        // mcr.sys says book 1, Windower reports book 3, the client itself is on book 7. Only the
        // last one is true whatever the player did � including picking a book from the game's menu.
        _temp.SetCurrentSet("aaaa1", 0);
        WriteLiveReport("aaaa1", "Tetsouo", book: 3, set: 1);
        _settings.SetName("aaaa1", "Tetsouo");

        var viewModel = new MainWindowViewModel(_settings)
        {
            ProbeRunningClients = () => ["Tetsouo"],
            ProbeOpenBooks = () => [new OpenBook("Tetsouo", "aaaa1", 7)],
            LiveStateFolder = Path.Combine(_temp.Root, "live"),
        };
        viewModel.Initialize();
        viewModel.ReadOpenBooks();

        var books = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();

        Assert.True(books.First(b => b.Info.Number == 7).IsOpenInGame);
        Assert.False(books.First(b => b.Info.Number == 3).IsOpenInGame);
        Assert.Contains("read from the client itself", books.First(b => b.Info.Number == 7).Detail,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkerFollowsTheClientFromOneBookToTheNext()
    {
        _settings.SetName("aaaa1", "Tetsouo");
        int book = 7;

        var viewModel = new MainWindowViewModel(_settings)
        {
            ProbeRunningClients = () => ["Tetsouo"],
            ProbeOpenBooks = () => [new OpenBook("Tetsouo", "aaaa1", book)],
            LiveStateFolder = Path.Combine(_temp.Root, "live"),
        };
        viewModel.Initialize();
        viewModel.ReadOpenBooks();

        var books = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();
        Assert.True(books.First(b => b.Info.Number == 7).IsOpenInGame);

        book = 23;
        viewModel.ReadOpenBooks();

        Assert.True(books.First(b => b.Info.Number == 23).IsOpenInGame);
        Assert.False(books.First(b => b.Info.Number == 7).IsOpenInGame);
    }

    [Fact]
    public void AWindowerReportBeatsAnythingWorkedOutFromTheFiles()
    {
        // mcr.sys says book 1 and no file has moved since, so the file-based answer would be book 1.
        _temp.SetCurrentSet("aaaa1", 0);
        _temp.AddCharacter("aaaa1", 20);                    // book 3 exists too
        WriteLiveReport("aaaa1", "Tetsouo", book: 3, set: 2);

        var books = NewViewModel().Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();

        Assert.True(books.First(b => b.Info.Number == 3).IsOpenInGame);
        Assert.False(books.First(b => b.Info.Number == 1).IsOpenInGame);
        Assert.Contains("reported by Windower", books.First(b => b.Info.Number == 3).Detail,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void NamingACharacterLinksItToTheWindowerReport()
    {
        // The addon knows the character by name; a USER folder is a number. Naming the folder is
        // what ties the two together, and the marker has to appear as soon as it is done.
        WriteLiveReport("does-not-match", "Tetsouo", book: 4, set: 1);
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1");
        Assert.False(character.Books.First(b => b.Info.Number == 4).IsOpenInGame);

        viewModel.BeginRename(character);
        character.RenameDraft = "Tetsouo";
        viewModel.CommitRename(character);

        Assert.True(character.Books.First(b => b.Info.Number == 4).IsOpenInGame);
        Assert.Equal("Tetsouo", _settings.NameFor("aaaa1"));
        Assert.Contains("Tetsouo", character.Header, StringComparison.Ordinal);

        // And it was written to the settings this test owns, not to the ones of whoever ran it.
        Assert.True(File.Exists(Path.Combine(_temp.Root, "settings.json")));
    }

    [Fact]
    public void AFolderRenamedOnDiskIsFlaggedAsInvisibleToTheGame()
    {
        // Renaming a USER folder to something readable is exactly the mistake this warns about: the
        // game looks the character up by the hexadecimal name it gave the folder, finds nothing, and
        // starts it again from empty macros.
        _temp.AddCharacter("Tetsouo", 0);
        var viewModel = NewViewModel();

        var renamed = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "Tetsouo");
        var untouched = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1");

        Assert.True(renamed.IsUnreachableByGame);
        Assert.Contains("the game will not find this folder", renamed.Detail, StringComparison.Ordinal);
        Assert.False(untouched.IsUnreachableByGame);
    }

    [Fact]
    public void AReportForNobodyKnownSaysWhichNameIsWaiting()
    {
        WriteLiveReport("does-not-match", "Tetsouo", book: 4, set: 1);
        var viewModel = NewViewModel();

        viewModel.ReadFolderBack();

        Assert.Contains("Tetsouo", viewModel.Status, StringComparison.Ordinal);
        Assert.Contains("F2", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkerFollowsTheAddonWhileThePlayerChangesBooks()
    {
        WriteLiveReport("aaaa1", "Tetsouo", book: 1, set: 1);
        var viewModel = NewViewModel();
        var books = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();
        Assert.True(books.First(b => b.Info.Number == 1).IsOpenInGame);

        WriteLiveReport("aaaa1", "Tetsouo", book: 4, set: 1);
        viewModel.ReadFolderBack();

        Assert.True(books.First(b => b.Info.Number == 4).IsOpenInGame);
        Assert.False(books.First(b => b.Info.Number == 1).IsOpenInGame);
    }

    [Fact]
    public void ALiveReportIsDroppedOnceTheClientWritesThatBook()
    {
        // Changing book from the game's own menu sends no command, so the addon never hears it. What
        // it does leave behind is the client writing the book it is leaving � proof the report is
        // now describing where the player was, not where they are.
        WriteLiveReport("aaaa1", "Tetsouo", book: 1, set: 1);
        var viewModel = NewViewModel();
        var books = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();
        Assert.True(books.First(b => b.Info.Number == 1).IsOpenInGame);

        _temp.Touch("aaaa1", 0, DateTime.UtcNow.AddMinutes(5));
        viewModel.ReadFolderBack();

        Assert.All(books, book => Assert.False(book.IsOpenInGame));
    }

    [Fact]
    public void ANonsensicalReportIsIgnoredRatherThanBelieved()
    {
        _temp.SetCurrentSet("aaaa1", 0);
        WriteLiveReport("aaaa1", "Tetsouo", book: 99, set: 1);   // no such book

        var books = NewViewModel().Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();

        Assert.True(books.First(b => b.Info.Number == 1).IsOpenInGame);   // back to mcr.sys
        Assert.Contains("where the game left", books.First(b => b.Info.Number == 1).Detail,
                        StringComparison.Ordinal);
    }

    /// <summary>Writes what the Windower addon writes: one small key=value file per character.</summary>
    private void WriteLiveReport(string characterId, string name, int book, int set)
    {
        string folder = Path.Combine(_temp.Root, "live");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, $"{name}.txt"),
            $"id={characterId}\nname={name}\nbook={book}\nset={set}\nat=1\n");
    }

    [Fact]
    public void TheMarkerIsDroppedAsSoonAsAnyBookIsWrittenAfterwards()
    {
        // A macro file written after mcr.sys means the client has been changing books since it
        // recorded that state — and where it went is written down nowhere.
        _temp.SetCurrentSet("aaaa1", 0);                    // the game is on book 1
        var viewModel = NewViewModel();
        var books = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();
        Assert.True(books.First(b => b.Info.Number == 1).IsOpenInGame);

        // The player moves on, and the client writes the book it is leaving.
        _temp.Touch("aaaa1", 1, DateTime.UtcNow.AddMinutes(5));
        viewModel.ReadFolderBack();

        Assert.All(books, book => Assert.False(book.IsOpenInGame));
    }

    // ---------------------------------------------------------------- following the game

    [Fact]
    public void TheMarkerFollowsWhatTheGameRecordsWhileTheAppIsOpen()
    {
        _temp.SetCurrentSet("aaaa1", 0);                              // the game is on book 1
        var viewModel = NewViewModel();
        var books = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.ToList();
        Assert.True(books.First(b => b.Info.Number == 1).IsOpenInGame);

        // The client saves its state again, this time parked on book 3.
        _temp.AddCharacter("aaaa1", 20);
        _temp.Touch("aaaa1", 20, DateTime.UtcNow.AddHours(-1));
        _temp.SetCurrentSet("aaaa1", 20);
        viewModel.ReadFolderBack();

        Assert.True(books.First(b => b.Info.Number == 3).IsOpenInGame);
        Assert.False(books.First(b => b.Info.Number == 1).IsOpenInGame);
    }

    [Fact]
    public void ASetRewrittenByTheGameIsPickedUp_WhenNothingIsUnsaved()
    {
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        Assert.Equal("BuffSelf", set.Macros[0].Name);

        RewriteOnDisk(set, "InGame");
        viewModel.ReadFolderBack();

        Assert.Equal("InGame", set.Macros[0].Name);
        Assert.False(set.IsDirty);
        Assert.Contains("rewritten by the game", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsavedEditsSurviveTheGameRewritingTheSameSet()
    {
        // The user's work always wins: the file changed underneath, but nothing here is thrown away.
        var viewModel = NewViewModel();
        var set = CurrentSet(viewModel);
        set.Macros[0].Name = "Mine";

        RewriteOnDisk(set, "InGame");
        viewModel.ReadFolderBack();

        Assert.Equal("Mine", set.Macros[0].Name);
        Assert.True(set.IsDirty);
    }

    [Fact]
    public void ABookRenamedByTheGameShowsItsNewTitle()
    {
        var viewModel = NewViewModel();
        var book = viewModel.Characters.OfType<CharacterNodeViewModel>()
            .First(c => c.Character.Id == "aaaa1").Books.First(b => b.Info.Number == 1);
        Assert.Equal("ThfRdm", book.Info.Title);

        // Something else — the client, or a second editor — writes the title file.
        var titles = FfxiMacros.Core.Discovery.CharacterTitles.Load(Path.Combine(_temp.UserFolder, "aaaa1"));
        titles[1] = "Renamed";
        titles.SaveHalfFor(1);

        viewModel.ReadFolderBack();

        Assert.Equal("Renamed", book.Info.Title);
    }

    /// <summary>Writes a set file behind the editor's back, the way the client does.</summary>
    private static void RewriteOnDisk(SetNodeViewModel set, string firstMacroName)
    {
        var book = MacroBookFile.Load(set.Info.FullPath);
        book.Macros[0].Name = firstMacroName;
        MacroBookFile.Save(book, set.Info.FullPath);
        File.SetLastWriteTimeUtc(set.Info.FullPath, DateTime.UtcNow.AddSeconds(2));
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
    public void ABookOperationWaitsForTheUnsavedEditsInsteadOfBeingRefused()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        CurrentSet(viewModel).Macros[0].Name = "Draft";

        viewModel.RequestBookTransfer(
            character.Books.First(b => b.Info.Number == 1),
            character.Books.First(b => b.Info.Number == 9),
            move: false);

        Assert.True(viewModel.HasPendingBookOperation);
        Assert.True(viewModel.PendingBookOperation!.NeedsSave);
        Assert.Contains("unsaved edits", viewModel.PendingBookOperation.Question, StringComparison.Ordinal);

        // Confirming is not the way out while edits are pending: saving them is.
        Assert.False(viewModel.ConfirmBookOperationCommand.CanExecute(null));
        Assert.True(viewModel.SaveAllAndConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void SavingEverythingCarriesTheWaitingBookOperationThrough()
    {
        var viewModel = NewViewModel();
        var character = viewModel.Characters.OfType<CharacterNodeViewModel>().First();
        var set = CurrentSet(viewModel);
        set.Macros[0].Name = "Draft";

        viewModel.CopyBookToClipboard(character.Books.First(b => b.Info.Number == 1));
        viewModel.PasteBookFromClipboard(character.Books.First(b => b.Info.Number == 9));
        viewModel.SaveAllAndConfirmCommand.Execute(null);

        Assert.False(viewModel.HasPendingBookOperation);
        Assert.Equal(0, viewModel.DirtyCount);

        // The edit was saved, then the book it belongs to was copied — so the copy carries it.
        var copied = viewModel.Characters.OfType<CharacterNodeViewModel>().First()
            .Books.First(b => b.Info.Number == 9);
        Assert.Equal(2, copied.Info.SetCount);
        Assert.Equal("Draft", MacroBookFile.Load(copied.Info.Set(1).FullPath).Macros[0].Name);
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
    public void BeingInGameIsOneLine_AndTheDetailStaysInTheTooltip()
    {
        var viewModel = NewViewModel("Kaelith");

        Assert.True(viewModel.IsGameRunning);
        Assert.Equal("In game: Kaelith", viewModel.GameStatusSummary);
        Assert.Contains("only holds the book shown on screen", viewModel.GameRunningWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningAFolderReportsWhatWasFound_NotWhoIsInGame()
    {
        // The client running is not an error, and it is not news either: it lives in the corner.
        var viewModel = NewViewModel("Kaelith");

        Assert.False(viewModel.StatusIsError);
        Assert.Contains("personnage(s)", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSummaryNamesEveryClientThatIsRunning()
    {
        var viewModel = NewViewModel("Kaelith", "Sylvane");

        Assert.Contains("Kaelith, Sylvane", viewModel.GameStatusSummary, StringComparison.Ordinal);
        Assert.Contains("Kaelith, Sylvane", viewModel.GameRunningWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggingOutClearsTheIndicator()
    {
        string[] running = ["Kaelith"];
        var viewModel = new MainWindowViewModel(_settings) { ProbeRunningClients = () => running };
        viewModel.Initialize();
        Assert.True(viewModel.IsGameRunning);

        running = [];
        viewModel.RecheckGameCommand.Execute(null);

        Assert.False(viewModel.IsGameRunning);
        Assert.Equal("", viewModel.GameStatusSummary);
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


