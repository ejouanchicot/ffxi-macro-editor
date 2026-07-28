using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Operations;
using FfxiMacros.Core.Model;
using Xunit;

namespace FfxiMacros.Tests;

public class MacroOperationsTests : IDisposable
{
    private readonly TempUserFolder _temp = new();

    private CharacterFolder Character(string id) =>
        MacroLibrary.Scan(_temp.UserFolder).ById(id)!;

    // ---------------------------------------------------------------- macros

    [Fact]
    public void CopyMacro_CopiesEveryFieldIncludingTheReservedBytes()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));
        var target = new Macro { Trailer = 9 };

        MacroOperations.CopyMacro(book.Macros[0], target);

        Assert.Equal("BuffSelf", target.Name);
        Assert.Equal("/con gs c smartbuff", target.Lines[0]);
        Assert.Equal(book.Macros[0].Header, target.Header);
        Assert.Equal(book.Macros[0].Trailer, target.Trailer);
    }

    [Fact]
    public void MoveMacro_EmptiesTheSource()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        MacroOperations.MoveMacro(book.Macros[0], book.Macros[19]);

        Assert.True(book.Macros[0].IsEmpty);
        Assert.Equal("BuffSelf", book.Macros[19].Name);
    }

    [Fact]
    public void SwapMacros_ExchangesThem()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));
        string first = book.Macros[0].Name;
        string second = book.Macros[1].Name;

        MacroOperations.SwapMacros(book.Macros[0], book.Macros[1]);

        Assert.Equal(second, book.Macros[0].Name);
        Assert.Equal(first, book.Macros[1].Name);
    }

    [Fact]
    public void CopyingAMacroOntoItselfChangesNothing()
    {
        var book = MacroBookFile.Load(SampleFiles.Path_("mcr.dat"));

        MacroOperations.CopyMacro(book.Macros[0], book.Macros[0]);
        MacroOperations.MoveMacro(book.Macros[0], book.Macros[0]);

        Assert.Equal("BuffSelf", book.Macros[0].Name);
    }

    // ---------------------------------------------------------------- sets

    [Fact]
    public void CopySet_ReproducesTheFileByteForByte()
    {
        _temp.AddCharacter("aaaa1", 0);
        var character = Character("aaaa1");

        MacroOperations.CopySet(character.Book(1).Set(1), character.Book(3).Set(5));

        Assert.True(character.Book(3).Set(5).Exists);
        Assert.Equal(
            File.ReadAllBytes(character.Book(1).Set(1).FullPath),
            File.ReadAllBytes(character.Book(3).Set(5).FullPath));
    }

    [Fact]
    public void CopySet_WorksBetweenCharacters()
    {
        _temp.AddCharacter("aaaa1", 0);
        _temp.AddCharacter("bbbb2", 0);
        var library = MacroLibrary.Scan(_temp.UserFolder);
        var source = library.ById("aaaa1")!.Book(1).Set(1);
        var target = library.ById("bbbb2")!.Book(7).Set(2);

        MacroOperations.CopySet(source, target);

        Assert.Equal("BuffSelf", MacroBookFile.Load(target.FullPath).Macros[0].Name);
    }

    [Fact]
    public void MoveSet_LeavesNothingBehind()
    {
        _temp.AddCharacter("aaaa1", 0);
        var character = Character("aaaa1");
        var source = character.Book(1).Set(1);
        string sourcePath = source.FullPath;

        MacroOperations.MoveSet(source, character.Book(2).Set(1));

        Assert.False(File.Exists(sourcePath));
        Assert.False(source.Exists);
        Assert.True(character.Book(2).Set(1).Exists);
    }

    [Fact]
    public void CopySet_RefusesToCopyASetOntoItself()
    {
        _temp.AddCharacter("aaaa1", 0);
        var set = Character("aaaa1").Book(1).Set(1);

        var ex = Assert.Throws<MacroFileException>(() => MacroOperations.CopySet(set, set));
        Assert.Contains("onto itself", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CopySet_RefusesASourceThatDoesNotExist()
    {
        _temp.AddCharacter("aaaa1", 0);
        var character = Character("aaaa1");

        Assert.Throws<MacroFileException>(() =>
            MacroOperations.CopySet(character.Book(5).Set(5), character.Book(6).Set(6)));
    }

    [Fact]
    public void CopySet_RefusesAFileOfTheWrongSize()
    {
        _temp.AddCharacter("aaaa1", 0);
        _temp.AddRawFile("aaaa1", "mcr5.dat", new byte[128]);
        var character = Character("aaaa1");

        var ex = Assert.Throws<MacroFileException>(() =>
            MacroOperations.CopySet(character.Book(1).Set(6), character.Book(2).Set(1)));
        Assert.Contains("128 bytes", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- books

    [Fact]
    public void CopyBook_CopiesEverySetAndTheTitle()
    {
        _temp.AddCharacter("aaaa1", 0, 1, 2);   // book 1, sets 1-3
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");

        MacroOperations.CopyBook(character.Book(1), character.Book(9));

        Assert.Equal(3, character.Book(9).SetCount);
        Assert.Equal("ThfRdm", character.Book(9).Title);
        Assert.Equal("ThfRdm", Character("aaaa1").Book(9).Title);   // written to the .ttl
    }

    [Fact]
    public void CopyBook_RemovesTargetSetsTheSourceDoesNotHave()
    {
        _temp.AddCharacter("aaaa1", 0);          // book 1: set 1 only
        _temp.AddCharacter("bbbb2", 10, 11, 12); // book 2: sets 1-3
        var library = MacroLibrary.Scan(_temp.UserFolder);
        var source = library.ById("aaaa1")!.Book(1);
        var target = library.ById("bbbb2")!.Book(2);
        Assert.Equal(3, target.SetCount);

        MacroOperations.CopyBook(source, target);

        Assert.Equal(1, target.SetCount);
        Assert.True(target.Set(1).Exists);
        Assert.False(target.Set(2).Exists);
    }

    [Fact]
    public void CopyBook_CanKeepTheTargetTitle()
    {
        _temp.AddCharacter("aaaa1", 0);
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");
        MacroOperations.RenameBook(character.Book(9), "Garder");

        MacroOperations.CopyBook(character.Book(1), character.Book(9), keepTargetTitle: true);

        Assert.Equal("Garder", character.Book(9).Title);
    }

    [Fact]
    public void MoveBook_ClearsTheSourceCompletely()
    {
        _temp.AddCharacter("aaaa1", 0, 1);
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");

        MacroOperations.MoveBook(character.Book(1), character.Book(30));

        Assert.Equal(0, character.Book(1).SetCount);
        Assert.True(character.Book(1).IsUntitled);
        Assert.Equal(2, character.Book(30).SetCount);
        Assert.Equal("ThfRdm", character.Book(30).Title);
    }

    [Fact]
    public void MoveBook_WorksBetweenCharacters()
    {
        _temp.AddCharacter("aaaa1", 0);
        _temp.AddTitles("aaaa1");
        _temp.AddCharacter("bbbb2", 0);
        var library = MacroLibrary.Scan(_temp.UserFolder);
        var source = library.ById("aaaa1")!.Book(1);
        var target = library.ById("bbbb2")!.Book(25);

        MacroOperations.MoveBook(source, target);

        Assert.Equal(0, source.SetCount);
        Assert.Equal(1, target.SetCount);
        Assert.Equal("ThfRdm", target.Title);
        // Book 25 lives in mcr_2.ttl; check it really reached the disk.
        Assert.Equal("ThfRdm", MacroLibrary.Scan(_temp.UserFolder).ById("bbbb2")!.Book(25).Title);
    }

    [Fact]
    public void CopyBook_RefusesToCopyABookOntoItself()
    {
        _temp.AddCharacter("aaaa1", 0);
        var book = Character("aaaa1").Book(1);

        Assert.Throws<MacroFileException>(() => MacroOperations.CopyBook(book, book));
    }

    [Fact]
    public void ClearBook_DeletesEverySetAndTheTitle()
    {
        _temp.AddCharacter("aaaa1", 0, 1, 2);
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");

        MacroOperations.ClearBook(character.Book(1));

        Assert.Equal(0, character.Book(1).SetCount);
        Assert.Equal("Book01", character.Book(1).Title);
    }

    [Fact]
    public void RenameBook_WritesTheTitleFile()
    {
        _temp.AddCharacter("aaaa1", 0);
        var character = Character("aaaa1");

        MacroOperations.RenameBook(character.Book(21), "BluNin");

        Assert.Equal("BluNin", MacroLibrary.Scan(_temp.UserFolder).ById("aaaa1")!.Book(21).Title);
    }

    [Fact]
    public void RenameBook_RefusesATitleThatDoesNotFit()
    {
        _temp.AddCharacter("aaaa1", 0);
        var character = Character("aaaa1");

        var ex = Assert.Throws<MacroFileException>(() =>
            MacroOperations.RenameBook(character.Book(1), "BeaucoupTropLongPourUnTitre"));
        Assert.Contains("15 bytes", ex.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _temp.Dispose();
}
