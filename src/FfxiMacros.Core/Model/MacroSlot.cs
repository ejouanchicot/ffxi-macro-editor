namespace FfxiMacros.Core.Model;

/// <summary>
/// Maps the 20 macro indexes of a set onto the in-game key palettes:
/// index 0-9 = Ctrl-1..Ctrl-0, index 10-19 = Alt-1..Alt-0.
/// </summary>
public static class MacroSlot
{
    public const int PaletteSize = 10;

    public static bool IsCtrl(int index) => index is >= 0 and < PaletteSize;

    public static bool IsAlt(int index) => index is >= PaletteSize and < MacroBook.MacroCount;

    /// <summary>The key digit shown in game: slots 1..9 then 0.</summary>
    public static char Key(int index)
    {
        Validate(index);
        int n = (index % PaletteSize) + 1;
        return n == 10 ? '0' : (char)('0' + n);
    }

    /// <summary>Human label such as <c>Ctrl-1</c> or <c>Alt-0</c>.</summary>
    public static string Describe(int index)
    {
        Validate(index);
        return $"{(IsCtrl(index) ? "Ctrl" : "Alt")}-{Key(index)}";
    }

    private static void Validate(int index)
    {
        if (index is < 0 or >= MacroBook.MacroCount)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"Macro index must be 0..{MacroBook.MacroCount - 1}.");
    }
}
