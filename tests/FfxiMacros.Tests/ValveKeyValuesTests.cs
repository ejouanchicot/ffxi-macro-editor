using FfxiMacros.Core.Discovery;
using Xunit;

namespace FfxiMacros.Tests;

public class ValveKeyValuesTests
{
    private const string ModernLibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"contentid"		"123456789"
        		"apps"
        		{
        			"228980"		"63229904"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"label"		""
        		"apps"
        		{
        		}
        	}
        }
        """;

    private const string LegacyLibraryFolders = """
        "LibraryFolders"
        {
        	"TimeNextStatsReport"		"1234"
        	"1"		"D:\\Steam"
        	"2"		"E:\\Games\\Steam"
        }
        """;

    [Fact]
    public void Parse_ReadsNestedLibraryPaths()
    {
        var root = ValveKeyValues.Parse(ModernLibraryFolders)["libraryfolders"];

        Assert.Equal(2, root.Children.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", root["0"]["path"].Value);
        Assert.Equal(@"D:\SteamLibrary", root["1"]["path"].Value);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnKeys()
    {
        var root = ValveKeyValues.Parse(LegacyLibraryFolders)["LIBRARYFOLDERS"];

        Assert.Equal(@"D:\Steam", root["1"].Value);
        Assert.Equal(@"E:\Games\Steam", root["2"].Value);
    }

    [Fact]
    public void Parse_UnescapesBackslashes()
    {
        var root = ValveKeyValues.Parse("\"a\" { \"path\" \"C:\\\\x\\\\y\" }");

        Assert.Equal(@"C:\x\y", root["a"]["path"].Value);
    }

    [Fact]
    public void Parse_SkipsComments()
    {
        var root = ValveKeyValues.Parse("""
            // a comment
            "a"
            {
                "b"  "1"   // trailing comment
            }
            """);

        Assert.Equal("1", root["a"]["b"].Value);
    }

    [Fact]
    public void MissingKeys_YieldAnEmptyNodeInsteadOfThrowing()
    {
        var root = ValveKeyValues.Parse(ModernLibraryFolders);

        Assert.Null(root["nope"]["deeper"]["deepest"].Value);
        Assert.Empty(root["nope"].Children);
    }

    [Fact]
    public void Parse_KeepsWhatItCanFromATruncatedFile()
    {
        var root = ValveKeyValues.Parse("""
            "libraryfolders"
            {
                "0" { "path" "D:\\Steam" }
                "1" { "path" "E:\\Broke
            """)["libraryfolders"];

        Assert.Equal(@"D:\Steam", root["0"]["path"].Value);
    }

    [Fact]
    public void Parse_OfGarbageReturnsAnEmptyTree()
    {
        Assert.Empty(ValveKeyValues.Parse("").Children);
        Assert.Empty(ValveKeyValues.Parse("}}}}").Children);
    }
}
