using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FfxiMacros.Tools.BookScanner;

/// <summary>
/// Finds where a running FFXI client keeps the macro book it has open.
/// </summary>
/// <remarks>
/// <para>
/// No public offset exists for it, and it would be tied to a game version anyway, so it is found by
/// elimination on the machine it runs on: read every address holding the book currently on screen,
/// have the player change book, keep only the addresses that followed. Two or three rounds leave a
/// handful, and one of them is the real one.
/// </para>
/// <para>
/// Read-only throughout. Nothing is written to the game's memory, and the process is opened with
/// exactly the two rights that reading requires.
/// </para>
/// </remarks>
internal static class Program
{
    private const string CandidateFile = "candidates.txt";

    private static int Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => Scan(Value(args, 1), Title(args)),
                "narrow" => Narrow(Value(args, 1), Title(args)),
                "watch" => Watch(Title(args)),
                "pointers" => Pointers(args, Title(args)),
                "follow" => Follow(args, Title(args)),
                "dump" => Dump(args, Title(args)),
                "signature" => Signature(args, Title(args)),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            bookscanner — finds the macro book a running FFXI client has open

              scan <book> [--title Name]     first round: every address holding that book number
              narrow <book> [--title Name]   later rounds: keep only the addresses that followed
              watch [--title Name]           print what the surviving addresses hold, as it changes

            A book is given as it reads in game, 1..40. Both that number and its zero-based form are
            looked for, because which one the client stores is exactly what this is here to find out.
            """);

        return 1;
    }

    private static int Value(string[] args, int index)
    {
        if (args.Length <= index || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new InvalidOperationException("A book number is needed, 1..40.");

        return value is >= 1 and <= 40 ? value : throw new InvalidOperationException("A book is 1..40.");
    }

    private static string? Title(string[] args)
    {
        int at = Array.FindIndex(args, a => a.Equals("--title", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && args.Length > at + 1 ? args[at + 1] : null;
    }

    // ---------------------------------------------------------------- the rounds

    private static int Scan(int book, string? title)
    {
        var client = Client(title);
        Console.WriteLine($"process {client.Id} “{client.MainWindowTitle}”, looking for book {book}");

        var found = ScanRegions(client, book);
        Save(found);

        Console.WriteLine($"{found.Count} address(es) hold {book} or {book - 1}.");
        Console.WriteLine("Now change to a different book in game, then run: narrow <that book>");
        return 0;
    }

    private static int Narrow(int book, string? title)
    {
        var previous = Load();
        if (previous.Count == 0)
        {
            Console.Error.WriteLine("No candidates from a previous round — run scan first.");
            return 2;
        }

        var client = Client(title);
        var still = new List<Candidate>();

        foreach (var candidate in previous)
        {
            int? value = ReadInt(client.Handle, candidate.Address, candidate.Width);
            if (value == book - candidate.Base)
                still.Add(candidate);
        }

        Save(still);
        Console.WriteLine($"{still.Count} of {previous.Count} address(es) followed to book {book}.");

        // An address inside a module is the only kind worth keeping: it can be written down as
        // module+offset and found again after a restart. Heap addresses move every launch.
        var inModules = still.Where(c => ModuleOf(client, c) is not null).ToList();
        Console.WriteLine($"{inModules.Count} of them sit inside a loaded module:");

        foreach (var candidate in inModules.Take(20))
            Console.WriteLine($"  {Describe(client, candidate)}");

        if (inModules.Count == 0)
            foreach (var candidate in still.Take(6))
                Console.WriteLine($"  {Describe(client, candidate)}");

        Console.WriteLine(still.Count switch
        {
            0 => "None left: the value is stored somewhere this did not look, or it moved.",
            1 => "One left. Run: watch",
            _ => "Change book again and run: narrow <that book>",
        });

        return 0;
    }

    private static int Watch(string? title)
    {
        var candidates = Load();
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine("No candidates — run scan first.");
            return 2;
        }

        var client = Client(title);
        Console.WriteLine($"watching {candidates.Count} address(es); change books in game, Ctrl+C to stop");

        var last = new Dictionary<ulong, int>();
        while (true)
        {
            foreach (var candidate in candidates)
            {
                int? value = ReadInt(client.Handle, candidate.Address, candidate.Width);
                if (value is null)
                    continue;

                int book = value.Value + candidate.Base;
                if (last.TryGetValue(candidate.Address, out int before) && before == book)
                    continue;

                last[candidate.Address] = book;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {Describe(client, candidate)} = book {book}");
            }

            Thread.Sleep(200);
        }
    }

    /// <summary>
    /// Finds what points at an address — the way back to something that survives a restart.
    /// </summary>
    /// <remarks>
    /// A value living in the heap has no fixed address: the client allocates it afresh every time
    /// it starts. What does hold still is the pointer the code follows to reach it, and a pointer
    /// stored inside a module is written down as module+offset. The search allows the address to
    /// sit a little way inside the structure being pointed at, which is the usual arrangement.
    /// </remarks>
    private static int Pointers(string[] args, string? title)
    {
        if (args.Length < 2 || !ulong.TryParse(args[1].TrimStart('0', 'x', 'X'), NumberStyles.HexNumber,
                                               CultureInfo.InvariantCulture, out ulong target))
        {
            Console.Error.WriteLine("An address is needed, in hexadecimal.");
            return 2;
        }

        int range = 0x800;
        int at = Array.FindIndex(args, a => a.Equals("--range", StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && args.Length > at + 1)
            range = int.Parse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture);

        var client = Client(title);
        Console.WriteLine($"looking for pointers into [0x{target - (ulong)range:X} .. 0x{target:X}]");

        var found = new List<(ulong At, ulong Value)>();
        var buffer = new byte[1 << 20];
        ulong address = 0;

        while (address < 0x7FFF_FFFF)
        {
            if (VirtualQueryEx(client.Handle, (IntPtr)address, out var region, (uint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
                break;

            ulong size = (ulong)region.RegionSize;
            if (size == 0)
                break;

            if (region.State == MemCommit && IsReadable(region.Protect))
            {
                for (ulong offset = 0; offset < size; offset += (ulong)buffer.Length)
                {
                    int length = (int)Math.Min((ulong)buffer.Length, size - offset);
                    ulong start = (ulong)region.BaseAddress + offset;

                    if (!ReadProcessMemory(client.Handle, (IntPtr)start, buffer, length, out int read) || read < 4)
                        continue;

                    for (int i = 0; i + 4 <= read; i += 4)
                    {
                        ulong value = BitConverter.ToUInt32(buffer.AsSpan(i));
                        if (value <= target && target - value <= (ulong)range)
                            found.Add((start + (ulong)i, value));
                    }
                }
            }

            address = (ulong)region.BaseAddress + size;
        }

        var inModules = found
            .Where(p => ModuleOf(client, new Candidate(p.At, 4, 0)) is not null)
            .ToList();

        Console.WriteLine($"{found.Count} pointer(s), of which {inModules.Count} inside a module:");
        foreach (var pointer in inModules.Take(20))
        {
            var module = ModuleOf(client, new Candidate(pointer.At, 4, 0))!;
            Console.WriteLine($"  {module.ModuleName}+0x{pointer.At - (ulong)module.BaseAddress:X}"
                              + $"  ->  0x{pointer.Value:X}   book at +0x{target - pointer.Value:X}");
        }

        if (inModules.Count == 0)
        {
            Console.WriteLine("None static. The first few heap pointers, to chase one level further:");
            foreach (var pointer in found.Take(8))
                Console.WriteLine($"  0x{pointer.At:X} -> 0x{pointer.Value:X}   (+0x{target - pointer.Value:X})");
        }

        return 0;
    }

    /// <summary>
    /// Walks a pointer path and prints what it lands on, over and over.
    /// </summary>
    /// <remarks>
    /// Usage: <c>follow FFXiMain.dll+0x630284 0x240 0x4</c> — the first argument names a place inside
    /// a module, which is the part that survives a restart; each offset after it is added to the
    /// value just read. The last hop is the one that holds the book, and what is printed is the
    /// whole neighbourhood of it, since a structure usually keeps the set right beside the book.
    /// </remarks>
    private static int Follow(string[] args, string? title)
    {
        if (args.Length < 2 || !args[1].Contains('+'))
        {
            Console.Error.WriteLine("Needs a module anchor, e.g. follow FFXiMain.dll+0x630284 0x240 0x4");
            return 2;
        }

        string[] anchor = args[1].Split('+');
        ulong anchorOffset = ulong.Parse(anchor[1].TrimStart('0', 'x', 'X'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        var hops = args.Skip(2)
            .TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => ulong.Parse(a.TrimStart('0', 'x', 'X'), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();

        var client = Client(title);
        var module = client.Modules.FirstOrDefault(m =>
            m.ModuleName.Equals(anchor[0], StringComparison.OrdinalIgnoreCase));

        if (module is null)
        {
            Console.Error.WriteLine($"{anchor[0]} is not loaded in that process.");
            return 2;
        }

        Console.WriteLine($"{anchor[0]} at 0x{(ulong)module.BaseAddress:X}; following +0x{anchorOffset:X}"
                          + string.Concat(hops.Select(h => $" -> +0x{h:X}")));
        Console.WriteLine("change books in game; Ctrl+C to stop");

        int last = int.MinValue;
        for (int i = 0; i < 600; i++)
        {
            ulong address = (ulong)module.BaseAddress + anchorOffset;
            bool broken = false;

            foreach (ulong hop in hops)
            {
                int? pointer = ReadInt(client.Handle, address, 4);
                if (pointer is null or 0)
                {
                    broken = true;
                    break;
                }

                address = (ulong)(uint)pointer.Value + hop;
            }

            int? value = broken ? null : ReadInt(client.Handle, address, 4);
            if (value is not null && value != last)
            {
                last = value.Value;
                int? before = ReadInt(client.Handle, address - 4, 4);
                int? after = ReadInt(client.Handle, address + 4, 4);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  0x{address:X} = {value} → book {value + 1}"
                                  + $"   (before: {before}, after: {after})");
            }

            Thread.Sleep(250);
        }

        return 0;
    }

    /// <summary>Prints the 32-bit words around an address, to see what the structure looks like.</summary>
    private static int Dump(string[] args, string? title)
    {
        if (args.Length < 2 || !ulong.TryParse(args[1].TrimStart('0', 'x', 'X'), NumberStyles.HexNumber,
                                               CultureInfo.InvariantCulture, out ulong address))
        {
            Console.Error.WriteLine("An address is needed, in hexadecimal.");
            return 2;
        }

        int words = args.Length > 2 && int.TryParse(args[2], out int n) ? n : 16;
        var client = Client(title);

        for (int i = -words; i <= words; i++)
        {
            ulong at = (ulong)((long)address + (i * 4));
            int? value = ReadInt(client.Handle, at, 4);
            Console.WriteLine($"  {(i * 4),+5:+#;-#;0}  0x{at:X}  {value,12}  0x{(uint)(value ?? 0):X8}");
        }

        return 0;
    }

    /// <summary>
    /// Finds the book table by its contents: the titles, sixteen bytes apart, with the book the
    /// player is on stored in the four bytes before them.
    /// </summary>
    /// <remarks>
    /// Nothing here is tied to a version of the game or to an address, which is what a pointer path
    /// could never manage: the titles come from the character's own <c>mcr.ttl</c>, so each client
    /// is recognised by what it is holding rather than by where it happens to be holding it.
    /// </remarks>
    private static int Signature(string[] args, string? title)
    {
        var wanted = args.Skip(1)
            .TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        if (wanted.Length < 2)
        {
            Console.Error.WriteLine("Give at least the first two book titles, e.g. signature THF DNC Book03");
            return 2;
        }

        var client = Client(title);
        Console.WriteLine($"process {client.Id} “{client.MainWindowTitle}”, looking for {string.Join(" / ", wanted)}");

        var buffer = new byte[1 << 20];
        ulong address = 0;
        int hits = 0;

        while (address < 0x7FFF_FFFF)
        {
            if (VirtualQueryEx(client.Handle, (IntPtr)address, out var region, (uint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
                break;

            ulong size = (ulong)region.RegionSize;
            if (size == 0)
                break;

            if (region.State == MemCommit && IsReadable(region.Protect))
            {
                for (ulong offset = 0; offset < size; offset += (ulong)buffer.Length - 1024)
                {
                    int length = (int)Math.Min((ulong)buffer.Length, size - offset);
                    ulong start = (ulong)region.BaseAddress + offset;

                    if (!ReadProcessMemory(client.Handle, (IntPtr)start, buffer, length, out int read) || read < 1024)
                        continue;

                    for (int i = 0; i + (wanted.Length * 16) + 8 < read; i += 2)
                    {
                        if (!Matches(buffer, i, wanted))
                            continue;

                        // Each entry is 16 bytes: two of something, then the title. The book the
                        // player is on sits six bytes before the first title.
                        ulong tableAt = start + (ulong)i;
                        int? book = ReadInt(client.Handle, tableAt - 6, 4);
                        int? stride = ReadInt(client.Handle, tableAt - 10, 4);

                        Console.WriteLine($"  table at 0x{tableAt:X}   book = {book} → book {book + 1}"
                                          + $"   (the value before it: {stride}"
                                          + $"{(book is not null && stride == book * 20 ? ", which is 20× that index" : "")})");
                        hits++;
                    }
                }
            }

            address = (ulong)region.BaseAddress + size;
        }

        Console.WriteLine(hits switch
        {
            0 => "Not found — the titles in memory are not the ones given.",
            1 => "One table, no ambiguity.",
            _ => $"{hits} places match; the client keeps more than one copy.",
        });

        return 0;
    }

    /// <summary>True when the titles sit at <paramref name="at"/>, sixteen bytes apart.</summary>
    private static bool Matches(byte[] data, int at, string[] titles)
    {
        for (int t = 0; t < titles.Length; t++)
        {
            int start = at + (t * 16);
            string title = titles[t];

            for (int c = 0; c < title.Length; c++)
            {
                if (data[start + c] != (byte)title[c])
                    return false;
            }

            if (data[start + title.Length] != 0)      // the title ends there, it is not a prefix
                return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- reading the process

    private sealed record Candidate(ulong Address, int Width, int Base);

    private sealed record Target(int Id, string MainWindowTitle, IntPtr Handle, ProcessModule[] Modules);

    private static Target Client(string? title)
    {
        var processes = Process.GetProcessesByName("pol")
            .Concat(Process.GetProcessesByName("ffximain"))
            .Where(p => title is null || p.MainWindowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (processes.Count == 0)
            throw new InvalidOperationException(title is null
                ? "No FFXI client is running."
                : $"No FFXI client whose window is called '{title}'.");

        if (processes.Count > 1)
            throw new InvalidOperationException(
                $"{processes.Count} clients running: pick one with --title ({string.Join(", ", processes.Select(p => p.MainWindowTitle))}).");

        var process = processes[0];
        IntPtr handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"Cannot read process {process.Id}: {Marshal.GetLastWin32Error()}.");

        return new Target(process.Id, process.MainWindowTitle, handle, process.Modules.Cast<ProcessModule>().ToArray());
    }

    /// <summary>Walks every committed, readable region and notes the addresses holding the value.</summary>
    private static List<Candidate> ScanRegions(Target client, int book)
    {
        var found = new List<Candidate>();
        var buffer = new byte[1 << 20];
        ulong address = 0;

        while (address < 0x7FFF_FFFF)
        {
            if (VirtualQueryEx(client.Handle, (IntPtr)address, out var region, (uint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
                break;

            ulong size = (ulong)region.RegionSize;
            if (size == 0)
                break;

            if (region.State == MemCommit && IsReadable(region.Protect))
            {
                for (ulong offset = 0; offset < size; offset += (ulong)buffer.Length)
                {
                    int length = (int)Math.Min((ulong)buffer.Length, size - offset);
                    ulong at = (ulong)region.BaseAddress + offset;

                    if (!ReadProcessMemory(client.Handle, (IntPtr)at, buffer, length, out int read) || read < 4)
                        continue;

                    Collect(found, buffer.AsSpan(0, read), at, book);
                }
            }

            address = (ulong)region.BaseAddress + size;
        }

        return found;
    }

    /// <summary>
    /// Notes 4-, 2- and 1-byte occurrences, of the book number and of its zero-based form.
    /// </summary>
    /// <remarks>
    /// Both widths and both bases are kept because guessing wrong on either would quietly rule out
    /// the very address being looked for. The rounds that follow throw away whatever was luck.
    /// </remarks>
    private static void Collect(List<Candidate> found, ReadOnlySpan<byte> data, ulong at, int book)
    {
        for (int i = 0; i + 4 <= data.Length; i += 4)
        {
            int value = BitConverter.ToInt32(data[i..]);
            if (value == book)
                found.Add(new Candidate(at + (ulong)i, 4, 0));
            else if (value == book - 1)
                found.Add(new Candidate(at + (ulong)i, 4, 1));
        }

        for (int i = 0; i + 2 <= data.Length; i += 2)
        {
            short value = BitConverter.ToInt16(data[i..]);
            if (value == book)
                found.Add(new Candidate(at + (ulong)i, 2, 0));
            else if (value == book - 1)
                found.Add(new Candidate(at + (ulong)i, 2, 1));
        }

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == book)
                found.Add(new Candidate(at + (ulong)i, 1, 0));
            else if (data[i] == book - 1)
                found.Add(new Candidate(at + (ulong)i, 1, 1));
        }
    }

    private static int? ReadInt(IntPtr handle, ulong address, int width)
    {
        var buffer = new byte[width];
        if (!ReadProcessMemory(handle, (IntPtr)address, buffer, width, out int read) || read != width)
            return null;

        return width switch
        {
            4 => BitConverter.ToInt32(buffer),
            2 => BitConverter.ToInt16(buffer),
            _ => buffer[0],
        };
    }

    private static ProcessModule? ModuleOf(Target client, Candidate candidate) =>
        client.Modules
            .Where(m => (ulong)m.BaseAddress <= candidate.Address
                        && candidate.Address < (ulong)m.BaseAddress + (ulong)m.ModuleMemorySize)
            .MaxBy(m => (ulong)m.BaseAddress);

    /// <summary>An address as a module plus an offset, which is what survives a restart.</summary>
    private static string Describe(Target client, Candidate candidate)
    {
        var module = ModuleOf(client, candidate);

        string where = module is null
            ? $"0x{candidate.Address:X} (not in a module — a heap address, useless across restarts)"
            : $"{module.ModuleName}+0x{candidate.Address - (ulong)module.BaseAddress:X}";

        return $"{where}  [{candidate.Width} byte(s), {(candidate.Base == 1 ? "zero-based" : "one-based")}]";
    }

    // ---------------------------------------------------------------- candidates on disk

    private static void Save(List<Candidate> candidates)
    {
        var text = new StringBuilder();
        foreach (var candidate in candidates)
            text.AppendLine($"{candidate.Address:X};{candidate.Width};{candidate.Base}");

        File.WriteAllText(CandidateFile, text.ToString());
    }

    private static List<Candidate> Load()
    {
        if (!File.Exists(CandidateFile))
            return [];

        return File.ReadAllLines(CandidateFile)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(';'))
            .Select(parts => new Candidate(
                ulong.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture)))
            .ToList();
    }

    // ---------------------------------------------------------------- win32

    private const int ProcessVmRead = 0x0010;
    private const int ProcessQueryInformation = 0x0400;
    private const int MemCommit = 0x1000;

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

    private sealed class Win32Exception(string message) : Exception(message);
}
