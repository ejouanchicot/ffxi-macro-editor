using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Io;

namespace FfxiMacros.Core.Discovery;

/// <summary>What a client is showing: whose macros, and which book of them.</summary>
public sealed record OpenBook(string ClientTitle, string CharacterId, int Book);

/// <summary>A character the probe knows how to recognise: its folder, and its book titles in order.</summary>
public sealed record CharacterSignature(string CharacterId, IReadOnlyList<string> Titles);

/// <summary>
/// Reads the macro book a client has open, out of its own memory.
/// </summary>
/// <remarks>
/// <para>
/// It is the only place that knows. The files say which book was <em>left</em> — the client writes
/// one on the way out — <c>mcr.sys</c> is written when it saves its state rather than when the
/// player turns a page, and Windower can only hear a <c>/macro book</c> command go past, which a
/// book picked from the game's own menu never sends.
/// </para>
/// <para>
/// The client keeps its forty book titles in a table, sixteen bytes an entry, with the book it is
/// showing in the four bytes ahead of it. So the table is found by its contents: the titles come
/// from the character's own <c>mcr.ttl</c>, and a client is recognised by what it holds rather than
/// by where it holds it. That costs a scan of the process, once, and buys three things a pointer
/// path could not — it survives a game update, it works for every client at once, and it says which
/// character each window belongs to without anyone having to declare it.
/// </para>
/// <para>
/// Read-only throughout: the process is opened for reading and querying, nothing else, and every
/// failure means "no reading available" rather than an error.
/// </para>
/// </remarks>
public sealed class GameMemoryProbe : IDisposable
{
    /// <summary>Entries of the title table, in bytes.</summary>
    private const int TitleStride = 16;

    /// <summary>Where a title sits, counted from the book number that precedes the table.</summary>
    private const int FirstTitleOffset = 6;

    private static readonly string[] ClientProcessNames = ["pol", "ffximain"];

    /// <summary>
    /// Guards what has been found so far.
    /// </summary>
    /// <remarks>
    /// Reading happens on the interface's thread, several times a second; the search that fills this
    /// in takes seconds and therefore runs on another. Two threads and a plain dictionary is how a
    /// window stops updating for reasons nobody can reproduce.
    /// </remarks>
    private readonly object _gate = new();

    private readonly Dictionary<int, Located> _located = [];
    private readonly Dictionary<int, DateTime> _lastScan = [];
    private readonly IMacroLog? _log;

    public GameMemoryProbe(IMacroLog? log = null) => _log = log;

    /// <summary>
    /// A table that has been found: where it is, whose it is, and the titles that identified it.
    /// </summary>
    /// <remarks>
    /// The titles are kept because they are what proves the address is still that table. Kept as
    /// they were when it was found, not as the folder holds them now: renaming a book in the editor
    /// does not change what a running client has in memory, and the reading must not stop for it.
    /// </remarks>
    private sealed record Located(ulong Address, string CharacterId, List<string> Titles);

    /// <summary>How long a client that could not be placed is left alone before trying again.</summary>
    public TimeSpan RescanAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Names the clients Windows would not let the editor read, or empty when there are none.
    /// </summary>
    /// <remarks>
    /// Reading another process needs the right to do so, and a client started with more privileges
    /// than the editor refuses it. That refusal used to be silent, which is the worst way to fail:
    /// the marker simply never appeared and nothing said why.
    /// </remarks>
    public IReadOnlyList<string> Unreadable
    {
        get
        {
            lock (_gate)
                return [.. _unreadable];
        }
    }

    private readonly SortedSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What every running client has open, reading only the addresses already found.
    /// </summary>
    /// <remarks>
    /// Cheap enough to call several times a second: eight bytes per client, and a check that the
    /// two of them still agree. A client that has not been placed yet is simply absent — placing it
    /// is <see cref="Scan"/>'s job, which takes long enough to belong on a thread of its own.
    /// </remarks>
    public IReadOnlyList<OpenBook> Read()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var found = new List<OpenBook>();

        foreach (var (process, handle) in OpenClients())
        {
            try
            {
                Located? located;
                lock (_gate)
                    _located.TryGetValue(process.Id, out located);

                if (located is not null && ReadBookAt(handle, located.Address, located.Titles) is { } book)
                {
                    found.Add(new OpenBook(process.MainWindowTitle, located.CharacterId, book));
                }
                else if (located is not null)
                {
                    // The table is no longer there: the client logged out, or reallocated. It will
                    // be looked for again rather than reported from an address that means nothing.
                    lock (_gate)
                        _located.Remove(process.Id);
                }
            }
            finally
            {
                CloseHandle(handle);
                process.Dispose();
            }
        }

        return found;
    }

    /// <summary>True when a client is running that has not been placed yet.</summary>
    public bool NeedsScan()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        foreach (var process in Clients())
        {
            try
            {
                lock (_gate)
                {
                    if (_located.ContainsKey(process.Id))
                        continue;

                    if (!_lastScan.TryGetValue(process.Id, out var when) || DateTime.UtcNow - when > RescanAfter)
                        return true;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// Walks the memory of every unplaced client, looking for the title table of one of the
    /// characters given. Slow — seconds — and meant for a background thread.
    /// </summary>
    public void Scan(IReadOnlyList<CharacterSignature> characters)
    {
        if (!OperatingSystem.IsWindows() || characters.Count == 0)
            return;

        var wanted = characters
            .Select(c => new { c.CharacterId, Titles = Searchable(c.Titles) })
            .Where(c => c.Titles.Count >= 2)
            .ToList();

        if (wanted.Count == 0)
            return;

        foreach (var (process, handle) in OpenClients())
        {
            try
            {
                lock (_gate)
                {
                    if (_located.ContainsKey(process.Id))
                        continue;

                    _lastScan[process.Id] = DateTime.UtcNow;
                }

                foreach (var character in wanted)
                {
                    if (FindTable(handle, character.Titles) is not { } address)
                        continue;

                    lock (_gate)
                        _located[process.Id] = new Located(address, character.CharacterId, character.Titles);

                    _log.Info($"'{process.MainWindowTitle}' is {character.CharacterId}; "
                              + $"its macro book reads at 0x{address:X}.");
                    break;
                }
            }
            finally
            {
                CloseHandle(handle);
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// The first few titles that can be looked for as they are: plain, non-empty, and ASCII.
    /// </summary>
    /// <remarks>
    /// Three is plenty — three names in a row, sixteen bytes apart, is not something a process holds
    /// by accident. Non-ASCII ones are left out rather than guessed at: how the client encodes them
    /// in memory is not something this needs to know to do its job.
    /// </remarks>
    private static List<string> Searchable(IReadOnlyList<string> titles) =>
        titles.Take(6)
            .Where(t => t.Length is > 0 and < TitleStride - FirstTitleOffset && t.All(c => c is > (char)0x20 and < (char)0x7F))
            .Take(3)
            .ToList();

    // ---------------------------------------------------------------- the scan itself

    [SupportedOSPlatform("windows")]
    private static ulong? FindTable(IntPtr handle, List<string> titles)
    {
        int span = (titles.Count * TitleStride) + TitleStride;
        var buffer = new byte[1 << 20];
        ulong address = 0;

        while (address < 0x7FFF_FFFF)
        {
            if (VirtualQueryEx(handle, (IntPtr)address, out var region, (uint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
                break;

            ulong size = (ulong)region.RegionSize;
            if (size == 0)
                break;

            if (region.State == MemCommit && IsReadable(region.Protect))
            {
                // Chunks overlap by a whole table, so one straddling a boundary is still seen.
                ulong step = (ulong)buffer.Length - (ulong)span;

                for (ulong offset = 0; offset < size; offset += step)
                {
                    int length = (int)Math.Min((ulong)buffer.Length, size - offset);
                    ulong start = (ulong)region.BaseAddress + offset;

                    if (!ReadProcessMemory(handle, (IntPtr)start, buffer, length, out int read) || read < span)
                        continue;

                    for (int i = FirstTitleOffset; i + span < read; i += 2)
                    {
                        if (!Matches(buffer, i, titles))
                            continue;

                        ulong book = start + (ulong)i - FirstTitleOffset;
                        if (ReadBookAt(handle, book, titles) is not null)
                            return book;
                    }
                }
            }

            address = (ulong)region.BaseAddress + size;
        }

        return null;
    }

    private static bool Matches(byte[] data, int at, List<string> titles)
    {
        for (int t = 0; t < titles.Count; t++)
        {
            int start = at + (t * TitleStride);
            string title = titles[t];

            for (int c = 0; c < title.Length; c++)
            {
                if (data[start + c] != (byte)title[c])
                    return false;
            }

            // The whole title, not a name that merely starts with it.
            if (data[start + title.Length] != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The book at an address, or null when the titles are no longer sitting behind it.
    /// </summary>
    /// <remarks>
    /// The titles are the proof, and they are checked on every reading. A book number alone would
    /// prove nothing — any four bytes holding 0 to 39 would pass, and a client that logs out leaves
    /// its memory to be reused by something else. The neighbouring field looked like twenty times
    /// the book index on the first readings taken; a second character showed it was not, which is
    /// exactly the kind of pattern that only holds until it is relied upon.
    /// </remarks>
    private static int? ReadBookAt(IntPtr handle, ulong address, List<string> titles)
    {
        int span = FirstTitleOffset + (titles.Count * TitleStride);
        var buffer = new byte[span];

        if (!ReadProcessMemory(handle, (IntPtr)address, buffer, span, out int read) || read != span)
            return null;

        if (!Matches(buffer, FirstTitleOffset, titles))
            return null;

        int index = BitConverter.ToInt32(buffer);
        return index >= 0 && index < MacroFileNaming.BooksPerCharacter ? index + 1 : null;
    }

    // ---------------------------------------------------------------- the processes

    private static IEnumerable<Process> Clients() =>
        ClientProcessNames.SelectMany(SafeProcessesByName)
            .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle));

    private void NoteUnreadable(string title)
    {
        bool first;
        lock (_gate)
            first = _unreadable.Add(title);

        if (first)
            _log.Warn($"Cannot read the memory of '{title}': access denied. "
                      + "The editor needs the same privileges as the client to see the book it has open.");
    }

    private void NoteReadable(string title)
    {
        lock (_gate)
            _unreadable.Remove(title);
    }

    [SupportedOSPlatform("windows")]
    private IEnumerable<(Process Process, IntPtr Handle)> OpenClients()
    {
        foreach (var process in Clients())
        {
            IntPtr handle;
            try
            {
                handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, process.Id);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                process.Dispose();
                continue;
            }

            if (handle == IntPtr.Zero)
            {
                if (Marshal.GetLastWin32Error() == ErrorAccessDenied)
                    NoteUnreadable(process.MainWindowTitle);

                process.Dispose();
                continue;
            }

            NoteReadable(process.MainWindowTitle);
            yield return (process, handle);
        }
    }

    private static IEnumerable<Process> SafeProcessesByName(string name)
    {
        try
        {
            return Process.GetProcessesByName(name);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            return [];
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _located.Clear();
            _lastScan.Clear();
        }
    }

    // ---------------------------------------------------------------- win32

    private const int ProcessVmRead = 0x0010;
    private const int ProcessQueryInformation = 0x0400;
    private const int MemCommit = 0x1000;
    private const int ErrorAccessDenied = 5;

    private static bool IsReadable(int protect) =>
        (protect & 0x100) == 0        // PAGE_GUARD: touching it would fault the game
        && (protect & (0x02 | 0x04 | 0x20 | 0x40)) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public int AllocationProtect;
        public int Alignment1;
        public IntPtr RegionSize;
        public int State;
        public int Protect;
        public int Type;
        public int Alignment2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
