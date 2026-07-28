using FfxiMacros.Core.Settings;
using Xunit;

namespace FfxiMacros.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ffxi-settings-{Guid.NewGuid():N}");

    private string Path_ => Path.Combine(_folder, "settings.json");

    [Fact]
    public void SaveThenLoad_KeepsEverything()
    {
        var settings = new EditorSettings { BackupBeforeSave = false, AlwaysLog = true };
        settings.UseUserFolder(@"D:\Steam\steamapps\common\FFXIPAL\SquareEnix\FINAL FANTASY XI\USER");
        settings.SetName("a1b2c3d", "Kaelith");

        SettingsStore.Save(settings, Path_);
        var loaded = SettingsStore.Load(Path_);

        Assert.Equal(settings.UserFolder, loaded.UserFolder);
        Assert.Equal("Kaelith", loaded.NameFor("a1b2c3d"));
        Assert.False(loaded.BackupBeforeSave);
        Assert.True(loaded.AlwaysLog);
        Assert.Equal(Path_, loaded.SourcePath);
    }

    [Fact]
    public void Load_OfAMissingFileReturnsDefaults()
    {
        var loaded = SettingsStore.Load(Path_);

        Assert.Null(loaded.UserFolder);
        Assert.Empty(loaded.CharacterNames);
        Assert.True(loaded.BackupBeforeSave);
    }

    [Fact]
    public void Load_OfACorruptFileReturnsDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "{ this is not json");

        var loaded = SettingsStore.Load(Path_);

        Assert.Null(loaded.UserFolder);
        Assert.Equal(Path_, loaded.SourcePath);
    }

    [Fact]
    public void Save_CreatesTheFolderItNeeds()
    {
        SettingsStore.Save(new EditorSettings(), Path.Combine(_folder, "nested", "settings.json"));

        Assert.True(File.Exists(Path.Combine(_folder, "nested", "settings.json")));
    }

    [Fact]
    public void CharacterNames_AreCaseInsensitiveAcrossASaveAndLoad()
    {
        var settings = new EditorSettings();
        settings.SetName("A1B2C3D", "Kaelith");
        SettingsStore.Save(settings, Path_);

        Assert.Equal("Kaelith", SettingsStore.Load(Path_).NameFor("a1b2c3d"));
    }

    [Fact]
    public void SetName_WithAnEmptyNameRemovesTheMapping()
    {
        var settings = new EditorSettings();
        settings.SetName("a1b2c3d", "Kaelith");
        settings.SetName("a1b2c3d", "   ");

        Assert.Null(settings.NameFor("a1b2c3d"));
        Assert.Empty(settings.CharacterNames);
    }

    [Fact]
    public void UseUserFolder_MovesAFolderBackToTheTopWithoutDuplicating()
    {
        var settings = new EditorSettings();
        settings.UseUserFolder(@"C:\A\USER");
        settings.UseUserFolder(@"C:\B\USER");
        settings.UseUserFolder(@"c:\a\user");

        Assert.Equal(@"c:\a\user", settings.UserFolder);
        Assert.Equal([@"c:\a\user", @"C:\B\USER"], settings.RecentUserFolders);
    }

    [Fact]
    public void UseUserFolder_CapsTheRecentList()
    {
        var settings = new EditorSettings();
        for (int i = 0; i < 12; i++)
            settings.UseUserFolder($@"C:\{i}\USER", maxRecent: 4);

        Assert.Equal(4, settings.RecentUserFolders.Count);
        Assert.Equal(@"C:\11\USER", settings.RecentUserFolders[0]);
    }

    [Fact]
    public void DefaultPaths_LiveUnderTheApplicationFolder()
    {
        Assert.StartsWith(SettingsStore.ApplicationFolder, SettingsStore.DefaultPath, StringComparison.Ordinal);
        Assert.StartsWith(SettingsStore.ApplicationFolder, SettingsStore.DefaultLogPath, StringComparison.Ordinal);
        Assert.StartsWith(SettingsStore.ApplicationFolder, SettingsStore.DefaultBackupFolder, StringComparison.Ordinal);
        Assert.EndsWith("FfxiMacroEditor", SettingsStore.ApplicationFolder, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
