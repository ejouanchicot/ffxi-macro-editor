using FfxiMacros.Core.Io;

namespace FfxiMacros.Tests;

/// <summary>
/// Builds a throwaway <c>USER</c> tree on disk from the real sample files, so discovery can be
/// tested without an FFXI install.
/// </summary>
public sealed class TempUserFolder : IDisposable
{
    public TempUserFolder(string? gameFolderName = null)
    {
        Root = Path.Combine(Path.GetTempPath(), $"ffxi-user-test-{Guid.NewGuid():N}");
        GameFolder = gameFolderName is null ? Root : Path.Combine(Root, gameFolderName);
        UserFolder = Path.Combine(GameFolder, "USER");
        Directory.CreateDirectory(UserFolder);
    }

    /// <summary>Temp root; everything lives underneath and is deleted on dispose.</summary>
    public string Root { get; }

    /// <summary>Stands in for <c>…\SquareEnix\FINAL FANTASY XI</c>.</summary>
    public string GameFolder { get; }

    public string UserFolder { get; }

    /// <summary>Creates a character folder holding the given macro set files, copied from the samples.</summary>
    public string AddCharacter(string id, params int[] fileIndexes) =>
        AddCharacterFrom(id, "mcr.dat", fileIndexes);

    /// <summary>Same, but from a chosen sample — some carry damage the others do not.</summary>
    public string AddCharacterFrom(string id, string sampleFile, params int[] fileIndexes)
    {
        string folder = Path.Combine(UserFolder, id);
        Directory.CreateDirectory(folder);

        foreach (int index in fileIndexes)
        {
            File.Copy(
                SampleFiles.Path_(sampleFile),
                Path.Combine(folder, MacroFileNaming.FileName(index)),
                overwrite: true);
        }

        return folder;
    }

    /// <summary>Copies the real title files into a character folder.</summary>
    public void AddTitles(string id, bool secondary = true)
    {
        string folder = Path.Combine(UserFolder, id);
        Directory.CreateDirectory(folder);
        File.Copy(SampleFiles.Path_("mcr.ttl"), Path.Combine(folder, "mcr.ttl"), overwrite: true);
        if (secondary)
            File.Copy(SampleFiles.Path_("mcr_2.ttl"), Path.Combine(folder, "mcr_2.ttl"), overwrite: true);
    }

    /// <summary>Drops an arbitrary file into a character folder, to check it is ignored.</summary>
    public string AddRawFile(string id, string fileName, byte[]? content = null)
    {
        string folder = Path.Combine(UserFolder, id);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, content ?? [0, 1, 2, 3]);
        return path;
    }

    /// <summary>Writes <c>mcr.sys</c>, the file where the game records the set a character is on.</summary>
    public void SetCurrentSet(string id, int fileIndex)
    {
        string folder = Path.Combine(UserFolder, id);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(
            Path.Combine(folder, MacroSystemFile.FileName),
            FfxiContainer.Write(1, BitConverter.GetBytes(fileIndex)));
    }

    public void Touch(string id, int fileIndex, DateTime whenUtc) =>
        File.SetLastWriteTimeUtc(Path.Combine(UserFolder, id, MacroFileNaming.FileName(fileIndex)), whenUtc);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
