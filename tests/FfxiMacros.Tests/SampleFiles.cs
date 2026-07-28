namespace FfxiMacros.Tests;

/// <summary>Locates the real game files copied next to the test binaries.</summary>
public static class SampleFiles
{
    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Samples");

    public static string Path_(string name) => Path.Combine(Directory, name);

    /// <summary>Every sample macro book, as xUnit member data.</summary>
    public static IEnumerable<object[]> Books =>
        System.IO.Directory.EnumerateFiles(Directory, "mcr*.dat")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new object[] { System.IO.Path.GetFileName(p) });

    /// <summary>Every sample title file, as xUnit member data.</summary>
    public static IEnumerable<object[]> TitleFiles =>
        System.IO.Directory.EnumerateFiles(Directory, "mcr*.ttl")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new object[] { System.IO.Path.GetFileName(p) });
}
