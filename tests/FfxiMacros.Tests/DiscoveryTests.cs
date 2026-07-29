using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Settings;
using Xunit;

namespace FfxiMacros.Tests;

public class DiscoveryTests
{
    private static (IMacroLog Log, List<string> Lines) CapturingLog()
    {
        var lines = new List<string>();
        return (new DelegateLog((level, message) => lines.Add($"{level}: {message}"), MacroLogLevel.Debug), lines);
    }

    // ---------------------------------------------------------------- locating USER

    [Fact]
    public void Resolve_AcceptsTheUserFolderItself()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("00410f9d", 0);

        Assert.Equal(temp.UserFolder, UserFolderLocator.Resolve(temp.UserFolder));
    }

    [Fact]
    public void Resolve_AcceptsTheGameFolderAboveIt()
    {
        using var temp = new TempUserFolder("FINAL FANTASY XI");
        temp.AddCharacter("00410f9d", 0);

        Assert.Equal(temp.UserFolder, UserFolderLocator.Resolve(temp.GameFolder));
    }

    [Fact]
    public void Resolve_AcceptsASingleCharacterFolder()
    {
        using var temp = new TempUserFolder();
        string character = temp.AddCharacter("00410f9d", 0);

        Assert.Equal(temp.UserFolder, UserFolderLocator.Resolve(character));
    }

    [Fact]
    public void Resolve_IgnoresATrailingSeparator()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("00410f9d", 0);

        Assert.Equal(temp.UserFolder, UserFolderLocator.Resolve(temp.UserFolder + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Resolve_RejectsAFolderWithoutCharacterData()
    {
        using var temp = new TempUserFolder();

        Assert.Null(UserFolderLocator.Resolve(temp.UserFolder));
        Assert.Null(UserFolderLocator.Resolve(Path.Combine(temp.Root, "does-not-exist")));
        Assert.Null(UserFolderLocator.Resolve(null));
        Assert.Null(UserFolderLocator.Resolve("   "));
    }

    [Fact]
    public void IsUserFolder_NeedsAtLeastOneCharacterFolder()
    {
        using var temp = new TempUserFolder();
        Assert.False(UserFolderLocator.IsUserFolder(temp.UserFolder));

        temp.AddCharacter("d4e5f6", 0);
        Assert.True(UserFolderLocator.IsUserFolder(temp.UserFolder));
    }

    [Fact]
    public void ACharacterFolderIsRecognisedByItsTitleFileAlone()
    {
        using var temp = new TempUserFolder();
        temp.AddTitles("e5f6a7");

        Assert.True(CharacterFolder.LooksLikeCharacterFolder(Path.Combine(temp.UserFolder, "e5f6a7")));
    }

    [Fact]
    public void Detect_FindsTheFolderNamedByTheEnvironmentVariable()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);

        var candidates = UserFolderLocator.Detect(temp.UserFolder);

        var mine = candidates.FirstOrDefault(c =>
            string.Equals(c.Path, temp.UserFolder, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mine);
        Assert.Equal(1, mine.CharacterCount);
        Assert.Equal(UserFolderSource.Configured, mine.Source);
    }

    [Fact]
    public void Detect_NeverThrowsWhenNothingIsInstalled()
    {
        var candidates = UserFolderLocator.Detect(@"Z:\nowhere\USER");

        Assert.DoesNotContain(candidates, c => c.Path.StartsWith(@"Z:\", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- scanning

    [Fact]
    public void Scan_PutsTheMainCharacterFirst_AndStillKnowsWhoWasPlayedLast()
    {
        // The order has to hold still and to be useful: sorting by the most recent write reshuffled
        // the list while two clients were running, and sorting by name buried the character with
        // four hundred macros under an alt with six. Who was played last is a question the library
        // still answers — it just no longer decides the order.
        using var temp = new TempUserFolder();
        temp.AddCharacter("aaaa1", 0, 1, 2);        // three sets: the main
        temp.AddCharacter("bbbb2", 0);              // one: an alt
        foreach (int file in new[] { 0, 1, 2 })
            temp.Touch("aaaa1", file, new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        temp.Touch("bbbb2", 0, new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc));

        var library = MacroLibrary.Scan(temp.UserFolder);

        Assert.Equal(["aaaa1", "bbbb2"], library.Characters.Select(c => c.Id));
        Assert.Equal("bbbb2", library.MostRecent!.Id);
    }

    [Fact]
    public void Scan_FallsBackToTheNameWhenTwoCharactersHoldAsMuch()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("zzzz9", 0);
        temp.AddCharacter("aaaa1", 0);

        var library = MacroLibrary.Scan(temp.UserFolder);

        Assert.Equal(["aaaa1", "zzzz9"], library.Characters.Select(c => c.Id));
    }

    [Fact]
    public void Scan_MapsFilesOntoBooksAndSets()
    {
        using var temp = new TempUserFolder();
        //           book 1 set 1   book 15 set 10   book 40 set 10
        temp.AddCharacter("a1b2c3d", 0, 149, 399);

        var character = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;

        Assert.Equal(40, character.Books.Length);
        Assert.Equal(3, character.SetFileCount);
        Assert.Equal(3, character.BookCount);

        Assert.True(character.Book(1).Set(1).Exists);
        Assert.False(character.Book(1).Set(2).Exists);
        Assert.True(character.Book(15).Set(10).Exists);
        Assert.True(character.Book(40).Set(10).Exists);
        Assert.Equal("mcr149.dat", character.Book(15).Set(10).FileName);
    }

    [Fact]
    public void Scan_IgnoresFilesThatAreNotMcrNumberDat_AndSaysSo()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        temp.AddRawFile("a1b2c3d", "mcrx.dat");
        temp.AddRawFile("a1b2c3d", "mcr07.dat");
        temp.AddRawFile("a1b2c3d", "cmb0.dat");     // not even mcr*: never looked at
        var (log, lines) = CapturingLog();

        var character = MacroLibrary.Scan(temp.UserFolder, log: log).ById("a1b2c3d")!;

        Assert.Equal(1, character.SetFileCount);
        Assert.Equal(["mcr07.dat", "mcrx.dat"], character.SkippedFiles.Order(StringComparer.Ordinal));
        Assert.Contains(lines, l => l.Contains("mcrx.dat", StringComparison.Ordinal) && l.Contains("not mcr#.dat", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("cmb0.dat", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_FlagsAMacroFileOfTheWrongSize()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        temp.AddRawFile("a1b2c3d", "mcr5.dat", new byte[100]);
        var (log, lines) = CapturingLog();

        var character = MacroLibrary.Scan(temp.UserFolder, log: log).ById("a1b2c3d")!;

        Assert.False(character.Book(1).Set(6).HasExpectedSize);
        Assert.Contains(lines, l => l.Contains("100 bytes instead of 7624", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_SkipsFoldersWithoutMacroFiles()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        Directory.CreateDirectory(Path.Combine(temp.UserFolder, "empty"));

        var library = MacroLibrary.Scan(temp.UserFolder);

        Assert.Single(library.Characters);
    }

    [Fact]
    public void Scan_KeepsAFolderWhoseNameIsNotHexadecimal()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("backup-of-kaelith", 0);

        var character = MacroLibrary.Scan(temp.UserFolder).ById("backup-of-kaelith")!;

        Assert.False(character.HasHexId);
        Assert.Equal(1, character.SetFileCount);
    }

    [Fact]
    public void Scan_AppliesTheReadableNamesFromSettings()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        var settings = new EditorSettings();
        settings.SetName("a1b2c3d", "Kaelith");

        var character = MacroLibrary.Scan(temp.UserFolder, settings).ById("a1b2c3d")!;

        Assert.Equal("Kaelith", character.DisplayName);
        Assert.Equal("Kaelith (a1b2c3d)", character.Label);
    }

    [Fact]
    public void Scan_ReportsAMissingFolderClearly()
    {
        var ex = Assert.Throws<MacroFileException>(() =>
            MacroLibrary.Scan(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}")));

        Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_ReportsAFolderThatIsNotAUserFolder()
    {
        using var temp = new TempUserFolder();

        var ex = Assert.Throws<MacroFileException>(() => MacroLibrary.Scan(temp.UserFolder));

        Assert.Contains("no character data", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- titles

    [Fact]
    public void Titles_SpanBothFilesAcrossTheFortyBooks()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        temp.AddTitles("a1b2c3d");

        var character = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;

        Assert.Equal("ThfRdm", character.Book(1).Title);
        Assert.Equal("PldBluC", character.Book(20).Title);
        Assert.Equal("PldRR", character.Book(21).Title);      // first entry of mcr_2.ttl
        Assert.Equal(40, character.Titles.All.Count());
    }

    [Fact]
    public void Titles_FallBackToTheGameDefaultsWhenTheFilesAreMissing()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);

        var character = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;

        Assert.False(character.Titles.PrimaryExisted);
        Assert.Equal("Book01", character.Book(1).Title);
        Assert.Equal("Book40", character.Book(40).Title);
        Assert.True(character.Book(1).IsUntitled);
    }

    [Fact]
    public void Titles_SurviveASaveAndReload()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        temp.AddTitles("a1b2c3d");

        var character = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;
        character.Book(3).Title = "WhmSch";
        character.Book(33).Title = "BluNin";
        character.Titles.Save();

        var reloaded = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;
        Assert.Equal("WhmSch", reloaded.Book(3).Title);
        Assert.Equal("BluNin", reloaded.Book(33).Title);
        Assert.Equal("ThfRdm", reloaded.Book(1).Title);
    }

    [Fact]
    public void Titles_AreCreatedOnDiskWhenTheFilesDidNotExist()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);

        var character = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;
        character.Book(1).Title = "ThfRdm";
        character.Titles.Save();

        Assert.True(File.Exists(character.Titles.PrimaryPath));
        Assert.Equal(BookTitleSet.FileSize, new FileInfo(character.Titles.PrimaryPath).Length);
        Assert.Equal("ThfRdm", MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!.Book(1).Title);
    }

    [Fact]
    public void Titles_IgnoreAnUnreadableFileInsteadOfFailingTheScan()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        temp.AddRawFile("a1b2c3d", "mcr.ttl", new byte[10]);
        var (log, lines) = CapturingLog();

        var character = MacroLibrary.Scan(temp.UserFolder, log: log).ById("a1b2c3d")!;

        Assert.Equal("Book01", character.Book(1).Title);
        Assert.Contains(lines, l => l.Contains("unreadable title file", StringComparison.Ordinal));
    }

    [Fact]
    public void Titles_RejectABookNumberOutsideOneToForty()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        var titles = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!.Titles;

        Assert.Throws<ArgumentOutOfRangeException>(() => titles[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => titles[41]);
    }

    // ---------------------------------------------------------------- backup

    [Fact]
    public void Backup_CopiesMacroAndTitleFilesOnly()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0, 149);
        temp.AddTitles("a1b2c3d");
        temp.AddRawFile("a1b2c3d", "cnf.dat");           // unrelated game data
        var character = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;

        string target = MacroLibrary.BackupCharacter(
            character, Path.Combine(temp.Root, "Backups"), new DateTime(2026, 7, 27, 15, 30, 0, DateTimeKind.Local));

        Assert.EndsWith("a1b2c3d-20260727-153000", target, StringComparison.Ordinal);
        Assert.Equal(
            ["mcr.dat", "mcr.ttl", "mcr149.dat", "mcr_2.ttl"],
            Directory.EnumerateFiles(target).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Backup_ProducesFilesIdenticalToTheOriginals()
    {
        using var temp = new TempUserFolder();
        temp.AddCharacter("a1b2c3d", 0);
        var character = MacroLibrary.Scan(temp.UserFolder).ById("a1b2c3d")!;

        string target = MacroLibrary.BackupCharacter(character, Path.Combine(temp.Root, "Backups"));

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(character.Path, "mcr.dat")),
            File.ReadAllBytes(Path.Combine(target, "mcr.dat")));
    }
}
