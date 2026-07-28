using System.Diagnostics;

namespace FfxiMacros.Core.Discovery;

/// <summary>
/// Detects a running FFXI client.
/// </summary>
/// <remarks>
/// The client reads <c>mcr*.dat</c> at login and keeps every macro in memory, then writes its own
/// copy back over the files. Saving from an editor while it runs is therefore lost work: the game
/// never sees the change, and overwrites it the next time it flushes. Observed first-hand — a set
/// saved at 18:34 came back one second later holding the client's version stamp and its old text.
/// </remarks>
public static class FfxiProcess
{
    /// <summary>Process names the client runs under; the PlayOnline launcher hosts the game itself.</summary>
    private static readonly string[] ProcessNames = ["pol", "ffxi", "ffximain"];

    /// <summary>
    /// The clients currently running, named by their window title when there is one — which for
    /// FFXI is the character name.
    /// </summary>
    public static IReadOnlyList<string> Running()
    {
        var found = new List<string>();

        foreach (string name in ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
            {
                continue;   // Never let process inspection break the editor.
            }

            foreach (var process in processes)
            {
                try
                {
                    string title = process.MainWindowTitle;
                    found.Add(string.IsNullOrWhiteSpace(title) ? name : title);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    found.Add(name);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return found;
    }

    public static bool IsRunning() => Running().Count > 0;

    /// <summary>
    /// Titles the client shows when nobody is logged in. FFXI names its window after the character
    /// once in game, and falls back to these on the login and character-select screens.
    /// </summary>
    private static readonly string[] LoggedOutTitles =
        ["final fantasy xi", "playonline", "playonline viewer", "ffxi"];

    /// <summary>
    /// The characters actually logged in — the only case where saving is unsafe.
    /// </summary>
    /// <remarks>
    /// A client sitting on the character-select screen has already flushed its macros to disk and
    /// let go of them, so editing then is fine; it only holds them while a character is in game.
    /// Observed directly: the window title switches from the character name back to
    /// "Final Fantasy XI" on logout, at the same moment the client rewrites its macro files.
    /// </remarks>
    public static IReadOnlyList<string> LoggedInCharacters() =>
        Running()
            .Where(title => !LoggedOutTitles.Contains(title.Trim(), StringComparer.OrdinalIgnoreCase))
            .ToList();
}
