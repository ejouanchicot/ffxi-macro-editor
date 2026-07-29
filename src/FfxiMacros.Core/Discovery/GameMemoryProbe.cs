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
    /// A run of book titles that identifies a character's table: which book it starts at, and the
    /// names in order from there.
    /// </summary>
    /// <remarks>
    /// The book it starts at matters as much as the names. They are matched sixteen bytes apart, so
    /// they have to be <em>consecutive</em> books — a signature built by picking out the usable
    /// titles and dropping the rest would be compared against positions its names never occupied.
    /// A character whose first book is nameless is not a reason to slide the others up; it is a
    /// reason to start the run at the second.
    /// </remarks>
    public sealed record TitleSignature(int FirstBook, List<byte[]> Titles)
    {
        /// <summary>The run as it reads, for logs and diagnostics.</summary>
        public IReadOnlyList<string> Names => [.. Titles.Select(t => Text.FfxiText.Decode(t))];

        /// <summary>Bytes from the start of the table to the first name of the run.</summary>
        public int Lead => FirstTitleOffset + ((FirstBook - 1) * TitleStride);

        /// <summary>Bytes that have to be read to check it.</summary>
        public int Span => Lead + (Titles.Count * TitleStride);

        /// <summary>
        /// The same run after one of its books was renamed, or null when it no longer proves
        /// anything — a name that cannot be matched byte for byte takes the run down with it.
        /// </summary>
        public TitleSignature? With(int bookNumber, string title)
        {
            int at = bookNumber - FirstBook;
            if (at < 0 || at >= Titles.Count)
                return this;                       // outside the run: it still says what it said

            byte[] renamedTo = Matchable(title);
            if (renamedTo.Length == 0)
                return null;

            var renamed = new List<byte[]>(Titles) { [at] = renamedTo };
            return this with { Titles = renamed };
        }
    }

    /// <summary>
    /// A table that has been found: where it is, whose it is, and the titles that identified it.
    /// </summary>
    /// <remarks>
    /// The titles are kept because they are what proves the address is still that table — which is
    /// why they follow every write this class makes to that table. Renaming a book in the editor
    /// does not change what a running client holds, but pushing that rename into the client does,
    /// and a proof that no longer matches costs a rescan. Worse than the delay: every write asked
    /// for during that rescan is refused, silently, for want of an address.
    /// </remarks>
    private sealed record Located(ulong Address, string CharacterId, TitleSignature Signature);

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

                if (located is not null && ReadBookAt(handle, located.Address, located.Signature) is { } book)
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
            .Select(c => new { c.CharacterId, Signature = SignatureFor(c.Titles) })
            .Where(c => c.Signature is not null)
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
                    if (FindTable(handle, character.Signature!) is not { } address)
                        continue;

                    lock (_gate)
                        _located[process.Id] = new Located(address, character.CharacterId, character.Signature!);

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

    /// <summary>How many books from the start are considered when building a signature.</summary>
    private const int SignatureDepth = 8;

    /// <summary>Names in the run. Three in a row is not something a process holds by accident.</summary>
    private const int SignatureLength = 3;

    /// <summary>Below this, the run proves too little to be trusted as an address.</summary>
    private const int MinimumSignature = 2;

    /// <summary>
    /// The first run of consecutive book names that can be looked for as they are.
    /// </summary>
    /// <remarks>
    /// Consecutive is the whole point: the names are matched sixteen bytes apart, so a run that
    /// skipped a nameless book in the middle would be looked for at positions it never occupied.
    /// A nameless book only moves where the run starts.
    /// </remarks>
    public static TitleSignature? SignatureFor(IReadOnlyList<string> titles)
    {
        var fields = titles.Select(Matchable).ToList();

        for (int first = 0; first < Math.Min(fields.Count, SignatureDepth); first++)
        {
            var run = fields.Skip(first).TakeWhile(f => f.Length > 0).Take(SignatureLength).ToList();
            if (run.Count >= MinimumSignature)
                return new TitleSignature(first + 1, run);
        }

        return null;
    }

    /// <summary>
    /// A name as the client holds it: the bytes the game wrote, up to the terminator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the string the editor shows. A book renamed and then cleared reads as
    /// <c>Book08{00}dm</c> — six letters, a terminator, and the tail of the name it used to have,
    /// written out with the terminator escaped so it can be seen. Looking for those thirteen
    /// characters in memory finds nothing, because the client holds nine bytes, one of them zero.
    /// </para>
    /// <para>
    /// So the name is encoded back into the field the game reads and taken up to its terminator.
    /// That also drops the ASCII rule the old signature needed: a name is matched as bytes, and how
    /// the game spells it is no longer this class's business. A field with no terminator at all is
    /// refused instead — the byte after the name is what proves it is the whole name.
    /// </para>
    /// </remarks>
    private static byte[] Matchable(string title)
    {
        byte[] field;
        try
        {
            field = BookTitleSet.EncodeTitle(title);
        }
        catch (Text.FfxiTextException)
        {
            return [];                                  // will not fit the field: not a name to look for
        }

        int end = Array.IndexOf(field, (byte)0);
        return end < 1 ? [] : field[..end];
    }

    // ---------------------------------------------------------------- the scan itself

    [SupportedOSPlatform("windows")]
    private static ulong? FindTable(IntPtr handle, TitleSignature signature)
    {
        int span = signature.Span + TitleStride;
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

                    // The run does not start at the table: what precedes it is the book number and
                    // however many books the run had to skip to find names it could match.
                    for (int i = signature.Lead; i + span < read; i += 2)
                    {
                        if (!Matches(buffer, i, signature.Titles))
                            continue;

                        ulong book = start + (ulong)i - (ulong)signature.Lead;
                        if (ReadBookAt(handle, book, signature) is not null)
                            return book;
                    }
                }
            }

            address = (ulong)region.BaseAddress + size;
        }

        return null;
    }

    private static bool Matches(byte[] data, int at, List<byte[]> titles)
    {
        for (int t = 0; t < titles.Count; t++)
        {
            int start = at + (t * TitleStride);
            byte[] title = titles[t];

            for (int c = 0; c < title.Length; c++)
            {
                if (data[start + c] != title[c])
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
    private static int? ReadBookAt(IntPtr handle, ulong address, TitleSignature signature)
    {
        int span = signature.Span;
        var buffer = new byte[span];

        if (!ReadProcessMemory(handle, (IntPtr)address, buffer, span, out int read) || read != span)
            return null;

        if (!Matches(buffer, signature.Lead, signature.Titles))
            return null;

        int index = BitConverter.ToInt32(buffer);
        return index >= 0 && index < MacroFileNaming.BooksPerCharacter ? index + 1 : null;
    }

    // ---------------------------------------------------------------- the processes

    private static IEnumerable<Process> Clients() =>
        ClientProcessNames.SelectMany(SafeProcessesByName)
            .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle));

    /// <summary>
    /// Writes a book's title into the memory of the client playing that character, so the game
    /// shows it at once instead of at the next login.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client reads the titles once, when the character logs in, and works from its own copy
    /// afterwards — which is why a rename only showed up after a relog. Worse, it writes that copy
    /// back to <c>mcr.ttl</c> when it saves its state: a title changed on disk during play could be
    /// undone on the way out. Writing both places is what makes the change hold.
    /// </para>
    /// <para>
    /// Every write is checked first. The field has to still hold the title the editor thinks is
    /// there, and the sixteen bytes written never reach past it. Anything else — a client that has
    /// moved on, a table that is no longer where it was — is refused rather than forced.
    /// </para>
    /// </remarks>
    /// <param name="expected">The title the editor believes is in that field right now.</param>
    /// <returns>True when the game's own copy was updated.</returns>
    public bool TryWriteTitle(string characterId, int bookNumber, string expected, string title, IMacroLog? log = null)
    {
        if (!OperatingSystem.IsWindows() || bookNumber is < 1 or > MacroFileNaming.BooksPerCharacter)
            return false;

        foreach (var (process, handle) in OpenClients(write: true))
        {
            try
            {
                Located? located;
                lock (_gate)
                    _located.TryGetValue(process.Id, out located);

                if (located is null
                    || !string.Equals(located.CharacterId, characterId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ulong field = located.Address + FirstTitleOffset + (ulong)((bookNumber - 1) * TitleStride);
                if (!Holds(handle, field, expected))
                {
                    log.Warn($"Not writing book {bookNumber} of {characterId} into the client: "
                             + $"the field no longer holds '{expected}'.");
                    return false;
                }

                byte[] bytes = BookTitleSet.EncodeTitle(title);
                if (!WriteProcessMemory(handle, (IntPtr)field, bytes, bytes.Length, out int written) || written != bytes.Length)
                {
                    log.Warn($"Could not write book {bookNumber} of {characterId} into the client: "
                             + $"error {Marshal.GetLastWin32Error()}.");
                    return false;
                }

                Remember(process.Id, located, located.Signature.With(bookNumber, title));
                log.Info($"Book {bookNumber} of {characterId} renamed to '{title}' in the running client.");
                return true;
            }
            finally
            {
                CloseHandle(handle);
                process.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// Writes every title of a character into its client, so the two copies cannot drift apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Titles have two owners: this editor writes the file, and the client works from a copy it
    /// took at login and writes back when it saves its state. A single write that is refused — the
    /// field no longer holding what was expected, the client having moved on — leaves the two
    /// disagreeing, and the client wins the moment it flushes. The names then no longer match the
    /// macros underneath them, which is precisely what happened to a set of books that had just
    /// been swapped.
    /// </para>
    /// <para>
    /// So after anything that changes titles, all forty are pushed rather than the one or two that
    /// changed. Six hundred and forty bytes, no guard to fail, and the file is the one telling the
    /// truth: the client ends up holding exactly what the disk holds.
    /// </para>
    /// </remarks>
    /// <returns>True when a client was found and written to.</returns>
    public bool TrySyncTitles(string characterId, IReadOnlyList<string> titles, IMacroLog? log = null)
    {
        if (!OperatingSystem.IsWindows() || titles.Count == 0)
            return false;

        foreach (var (process, handle) in OpenClients(write: true))
        {
            try
            {
                Located? located;
                lock (_gate)
                    _located.TryGetValue(process.Id, out located);

                if (located is null
                    || !string.Equals(located.CharacterId, characterId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int count = Math.Min(titles.Count, MacroFileNaming.BooksPerCharacter);
                var block = new byte[count * TitleStride];
                for (int book = 0; book < count; book++)
                    BookTitleSet.EncodeTitle(titles[book]).CopyTo(block, book * TitleStride);

                ulong field = located.Address + FirstTitleOffset;
                if (!WriteProcessMemory(handle, (IntPtr)field, block, block.Length, out int written)
                    || written != block.Length)
                {
                    log.Warn($"Could not sync the titles of {characterId} into the client: "
                             + $"error {Marshal.GetLastWin32Error()}.");
                    return false;
                }

                Remember(process.Id, located, SignatureFor(titles));
                log.Info($"Pushed {count} book titles of {characterId} into the running client.");
                return true;
            }
            finally
            {
                CloseHandle(handle);
                process.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps the proof of an address in step with what has just been written into it.
    /// </summary>
    /// <remarks>
    /// This class recognises a client by the book names in its table, and it also writes that table.
    /// Left alone, every successful write invalidated the very proof the next reading depends on:
    /// the client was forgotten half a second later, and the scan that places it again takes seconds
    /// during which nothing can be written at all. A player reorganising several books in a row
    /// therefore had some of them pushed to the client and some of them silently dropped — and the
    /// client, which rewrites <c>mcr.ttl</c> from its own copy when it saves, put the dropped names
    /// back over the file. The macros had moved; the names had not. This is that hole.
    /// </remarks>
    private void Remember(int processId, Located located, TitleSignature? signature)
    {
        lock (_gate)
        {
            // No run of names left to recognise it by — better forgotten than trusted blindly.
            if (signature is null)
                _located.Remove(processId);
            else
                _located[processId] = located with { Signature = signature };
        }
    }

    /// <summary>True when the field still spells what the editor expects, terminator included.</summary>
    private static bool Holds(IntPtr handle, ulong field, string expected)
    {
        var buffer = new byte[TitleStride];
        if (!ReadProcessMemory(handle, (IntPtr)field, buffer, buffer.Length, out int read) || read != buffer.Length)
            return false;

        byte[] wanted = BookTitleSet.EncodeTitle(expected);
        int length = Array.IndexOf(wanted, (byte)0);
        length = length < 0 ? wanted.Length : length;

        for (int i = 0; i < length; i++)
        {
            if (buffer[i] != wanted[i])
                return false;
        }

        return length == TitleStride || buffer[length] == 0;
    }

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

    /// <summary>
    /// Opens every running client. Write rights are asked for only when something is to be written,
    /// so the ordinary case — reading which book is open — holds no more power than it needs.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private IEnumerable<(Process Process, IntPtr Handle)> OpenClients(bool write = false)
    {
        int access = ProcessVmRead | ProcessQueryInformation | (write ? ProcessVmWrite | ProcessVmOperation : 0);

        foreach (var process in Clients())
        {
            IntPtr handle;
            try
            {
                handle = OpenProcess(access, false, process.Id);
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
    private const int ProcessVmWrite = 0x0020;
    private const int ProcessVmOperation = 0x0008;
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
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
