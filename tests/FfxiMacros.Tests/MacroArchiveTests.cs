using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// The archive that keeps a book rather than describes it: the game's own files, byte for byte.
/// The text export is for reading and for sending a macro to someone; this is what you reach for
/// when the point is to put a book back exactly as it was.
/// </summary>
public class MacroArchiveTests : IDisposable
{
    private readonly TempUserFolder _temp = new();

    private CharacterFolder Character(string id) => MacroLibrary.Scan(_temp.UserFolder).ById(id)!;

    [Fact]
    public void ABookSurvivesAnArchiveByteForByte()
    {
        _temp.AddCharacter("aaaa1", 0, 1);
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");
        string path = Path.Combine(_temp.Root, "book1" + MacroArchive.FileExtension);
        byte[] original = File.ReadAllBytes(character.Book(1).Set(1).FullPath);

        int exported = MacroArchive.Export(character.Book(1), path);
        int restored = MacroArchive.Import(path, character.Book(30));

        Assert.Equal(2, exported);
        Assert.Equal(2, restored);
        Assert.Equal(original, File.ReadAllBytes(character.Book(30).Set(1).FullPath));
        Assert.Equal("ThfRdm", character.Book(30).Title);           // the name travels with it
        Assert.Equal(2, character.Book(1).SetCount);                // and the source is untouched
    }

    [Fact]
    public void SetsAreRestoredByTheirPositionInTheBook()
    {
        // Set 2 of the book that was archived becomes set 2 of the book it lands in, whichever book
        // that is — the file names differ from one book to the next, the positions do not.
        _temp.AddCharacter("aaaa1", 1);            // book 1, set 2 only
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");
        string path = Path.Combine(_temp.Root, "book1" + MacroArchive.FileExtension);

        MacroArchive.Export(character.Book(1), path);
        MacroArchive.Import(path, character.Book(7));

        Assert.True(character.Book(7).Set(2).Exists);
        Assert.False(character.Book(7).Set(1).Exists);
    }

    [Fact]
    public void RestoringLeavesTheBookAsACopy_NotAMixture()
    {
        // The destination had three sets; the archive holds one. Keeping the other two would leave
        // a book that never existed anywhere.
        _temp.AddCharacter("aaaa1", 0, 60, 61, 62);
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");
        string path = Path.Combine(_temp.Root, "book1" + MacroArchive.FileExtension);

        MacroArchive.Export(character.Book(1), path);
        MacroArchive.Import(path, character.Book(7));

        Assert.Equal(1, character.Book(7).SetCount);
    }

    [Fact]
    public void AnArchiveSaysWhatItHoldsBeforeAnythingIsWritten()
    {
        _temp.AddCharacter("aaaa1", 0, 1);
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");
        string path = Path.Combine(_temp.Root, "book1" + MacroArchive.FileExtension);
        MacroArchive.Export(character.Book(1), path);

        var contents = MacroArchive.Read(path);

        Assert.Equal("aaaa1", contents.Character);
        Assert.Equal(1, contents.Book);
        Assert.Equal("ThfRdm", contents.Title);
        Assert.Equal(2, contents.SetCount);
    }

    [Fact]
    public void TheTitleCanBeLeftAloneWhenRestoring()
    {
        _temp.AddCharacter("aaaa1", 0);
        _temp.AddTitles("aaaa1");
        var character = Character("aaaa1");
        string path = Path.Combine(_temp.Root, "book1" + MacroArchive.FileExtension);
        MacroArchive.Export(character.Book(1), path);
        string kept = character.Book(9).Title;

        MacroArchive.Import(path, character.Book(9), keepTargetTitle: true);

        Assert.Equal(kept, character.Book(9).Title);
        Assert.Equal(1, character.Book(9).SetCount);
    }

    [Fact]
    public void EverythingMeansEverySetOfEveryCharacter_TitlesIncluded()
    {
        // What you want before a reorganisation, as against the per-book archive which is for
        // carrying one book somewhere. The titles matter as much as the sets: they are the only
        // part the set files do not carry.
        _temp.AddCharacter("aaaa1", 0, 1, 140);
        _temp.AddTitles("aaaa1");
        _temp.AddCharacter("bbbb2", 0);
        string path = Path.Combine(_temp.Root, "all" + MacroArchive.FileExtension);

        int files = MacroArchive.ExportEverything(MacroLibrary.Scan(_temp.UserFolder).Characters, path);

        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Equal(6, files);                                  // 4 sets + 2 title files
        Assert.Contains("aaaa1/mcr140.dat", names);              // laid out per character
        Assert.Contains("aaaa1/mcr.ttl", names);
        Assert.Contains("bbbb2/mcr.dat", names);
    }

    [Fact]
    public void SomethingThatIsNotAnArchiveIsRefusedRatherThanHalfRead()
    {
        string path = Path.Combine(_temp.Root, "nonsense" + MacroArchive.FileExtension);
        File.WriteAllText(path, "this is not a zip");
        _temp.AddCharacter("aaaa1", 0);

        Assert.Throws<MacroFileException>(() => MacroArchive.Read(path));
        Assert.Throws<MacroFileException>(() => MacroArchive.Import(path, Character("aaaa1").Book(2)));
    }

    public void Dispose() => _temp.Dispose();
}
