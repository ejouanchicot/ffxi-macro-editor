using System.Globalization;
using FfxiMacros.Core.Diagnostics;
using FfxiMacros.Core.Discovery;
using FfxiMacros.Core.GameData;
using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;
using FfxiMacros.Core.Operations;
using FfxiMacros.Core.Settings;
using FfxiMacros.Core.Text;

namespace FfxiMacros.Cli;

/// <summary>
/// Diagnostic front end for the core library: find the game folder, browse characters and books,
/// inspect a macro file, and verify that a whole USER folder survives a load/save round trip.
/// </summary>
internal static class Program
{
    private static IMacroLog? _log;
    private static EditorSettings _settings = new();

    private static int Main(string[] args)
    {
        bool debug = args.Contains("--debug", StringComparer.OrdinalIgnoreCase);
        args = args.Where(a => !a.Equals("--debug", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (args.Length == 0)
            return PrintUsage();

        FileLog? fileLog = null;
        try
        {
            _settings = SettingsStore.Load();

            if (debug || _settings.AlwaysLog)
            {
                fileLog = new FileLog(SettingsStore.DefaultLogPath);
                if (fileLog.OpenError is not null)
                    Console.Error.WriteLine($"warning: no log file ({fileLog.OpenError}).");
                else
                    Console.Error.WriteLine($"logging to {fileLog.Path}");

                _log = new CompositeLog(
                    fileLog,
                    new DelegateLog((level, message) => Console.Error.WriteLine($"  [{level}] {message}"),
                        debug ? MacroLogLevel.Debug : MacroLogLevel.Warning));
            }

            FfxiText.DefaultAutoTranslate = AutoTranslateDictionary.AutoLoad(
                FfxiDatIndex.InstallRootFor(_settings.UserFolder), _settings.WindowerFolder, _log);

            return args[0].ToLowerInvariant() switch
            {
                "find" => Find(),
                "list" => List(args),
                "books" => Books(args),
                "name" => Name(args),
                "config" => args.Length > 1 ? SetUserFolder(args[1]) : Config(),
                "backup" => Backup(args),
                "repair" => Repair(args),
                "show" => Show(args),
                "dump" => Dump(args),
                "diff" => Diff(args),
                "verify" => Verify(args),
                "titles" => Titles(args),
                "-h" or "--help" or "help" => PrintUsage(),
                _ => Fail($"Unknown command '{args[0]}'."),
            };
        }
        catch (MacroFileException ex)
        {
            Console.Error.WriteLine($"error: {ex}");
            _log.Error(ex.ToString());
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        finally
        {
            fileLog?.Dispose();
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            ffximacro — FFXI macro file inspector

            Finding the game
              find                           detect every FFXI USER folder on this machine
              config                         show the settings file and the current USER folder
              config <path>                  set (and remember) the USER folder

            Browsing
              list                           list the characters of the current USER folder
              books  <character>             list the 40 books of a character and their sets
              name   <character> <name>      attach a readable name to a character folder
              backup <character>             copy a character's macro files to the backup folder
              repair <character> [--apply]   restore the leading '/' the 2014 editor overwrote
                     [--replace old=new]     also fix a mistyped word, whole words only

            Files
              show   <mcr*.dat>              list the 20 macros of a set
              dump   <file> [offset] [len]   hex dump
              diff   <fileA> <fileB>         byte-level comparison
              verify <file|folder>           check the load/save round trip is byte-exact
              titles <mcr.ttl|mcr_2.ttl>     list the 20 book titles of a title file

            <character> is a folder id (a1b2c3d), a name you set with 'name', or a full path.
            Add --debug to write a log file and echo it to stderr.
            """);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }

    // ---------------------------------------------------------------- discovery

    private static int Find()
    {
        var candidates = UserFolderLocator.Detect(_settings.UserFolder, _log);
        if (candidates.Count == 0)
        {
            Console.WriteLine("No FFXI USER folder found. Set one with:  ffximacro config <path>");
            return 1;
        }

        Console.WriteLine($"{candidates.Count} USER folder(s) found:\n");
        foreach (var candidate in candidates)
        {
            bool current = string.Equals(candidate.Path, _settings.UserFolder, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  {(current ? "*" : " ")} {candidate.Path}");
            Console.WriteLine($"      {candidate.CharacterCount} character(s), detected via {candidate.Source}");
        }

        if (_settings.UserFolder is null)
            Console.WriteLine($"\nNone selected yet. Run:  ffximacro config \"{candidates[0].Path}\"");

        return 0;
    }

    private static int Config()
    {
        Console.WriteLine($"settings   {SettingsStore.DefaultPath}");
        Console.WriteLine($"log        {SettingsStore.DefaultLogPath}");
        Console.WriteLine($"backups    {_settings.BackupFolder ?? SettingsStore.DefaultBackupFolder}");
        Console.WriteLine($"USER       {_settings.UserFolder ?? "(not set — run 'find')"}");

        if (_settings.RecentUserFolders.Count > 1)
        {
            Console.WriteLine("recent:");
            foreach (string path in _settings.RecentUserFolders.Skip(1))
                Console.WriteLine($"  {path}");
        }

        if (_settings.CharacterNames.Count > 0)
        {
            Console.WriteLine("names:");
            foreach (var (id, name) in _settings.CharacterNames.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"  {id,-10} {name}");
        }

        return 0;
    }

    private static int SetUserFolder(string path)
    {
        string? resolved = UserFolderLocator.Resolve(path);
        if (resolved is null)
            return Fail($"'{path}' is not an FFXI USER folder (no character data inside).");

        _settings.UseUserFolder(resolved);
        SettingsStore.Save(_settings);
        Console.WriteLine($"USER folder set to {resolved}");
        return 0;
    }

    private static int List(string[] args)
    {
        var library = OpenLibrary(args.Length > 1 ? args[1] : null);
        if (library is null)
            return 1;

        Console.WriteLine($"{library.UserFolder}\n");
        Console.WriteLine($"  {"character",-24} {"books",5} {"sets",5}  last played");

        foreach (var character in library.Characters)
        {
            string when = character.LastWriteUtc == DateTime.MinValue
                ? "never"
                : character.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Console.WriteLine($"  {character.Label,-24} {character.BookCount,5} {character.SetFileCount,5}  {when}");
        }

        if (library.Characters.Count == 0)
            Console.WriteLine("  (no character folders found)");

        return 0;
    }

    private static int Books(string[] args)
    {
        if (args.Length < 2)
            return Fail("books needs a character.");

        var character = FindCharacter(args[1]);
        if (character is null)
            return 1;

        Console.WriteLine($"{character.Label}  —  {character.Path}");
        if (!character.Titles.PrimaryExisted && !character.Titles.SecondaryExisted)
            Console.WriteLine("  (no title files: showing the game's default book names)");
        Console.WriteLine();

        foreach (var book in character.Books)
        {
            if (!book.Exists && book.IsUntitled)
                continue;

            string sets = string.Concat(book.Sets.Select(s => s.Exists ? s.SetNumber.ToString(CultureInfo.InvariantCulture)[^1] : '.'));
            string when = book.LastWriteUtc == DateTime.MinValue
                ? ""
                : book.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Console.WriteLine($"  Book {book.Number,2}  {book.Title,-16} sets [{sets}]  {when}");
        }

        return 0;
    }

    private static int Name(string[] args)
    {
        if (args.Length < 3)
            return Fail("name needs a character folder and a name.");

        var character = FindCharacter(args[1]);
        if (character is null)
            return 1;

        _settings.SetName(character.Id, args[2]);
        SettingsStore.Save(_settings);
        Console.WriteLine($"{character.Id} is now known as {args[2]}.");
        return 0;
    }

    private static int Backup(string[] args)
    {
        if (args.Length < 2)
            return Fail("backup needs a character.");

        var character = FindCharacter(args[1]);
        if (character is null)
            return 1;

        string target = MacroLibrary.BackupCharacter(
            character, _settings.BackupFolder ?? SettingsStore.DefaultBackupFolder, log: _log);
        Console.WriteLine($"Backed up to {target}");
        return 0;
    }

    /// <summary>
    /// Restores the leading <c>/</c> the 2014 editor overwrote with a NUL, across a whole character.
    /// Lists what it would change and writes nothing unless <c>--apply</c> is given: a repaired line
    /// starts running in game, which is a change the user has to want.
    /// </summary>
    private static int Repair(string[] args)
    {
        bool apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);

        // --replace old=new, repeatable: fixes typos that went unnoticed because the line never ran.
        var substitutions = new List<(string Old, string New)>();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--replace", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] parts = args[i + 1].Split('=', 2);
            if (parts.Length != 2 || parts[0].Length == 0)
                return Fail($"--replace attend 'ancien=nouveau', pas '{args[i + 1]}'.");

            substitutions.Add((parts[0], parts[1]));
        }

        var skip = new HashSet<int>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--apply", StringComparison.OrdinalIgnoreCase))
                skip.Add(i);
            else if (args[i].Equals("--replace", StringComparison.OrdinalIgnoreCase))
            {
                skip.Add(i);
                skip.Add(i + 1);
            }
        }
        args = args.Where((_, i) => !skip.Contains(i)).ToArray();

        if (args.Length < 2)
            return Fail("repair needs a character.");

        var character = FindCharacter(args[1]);
        if (character is null)
            return 1;

        if (apply && FfxiProcess.LoggedInCharacters() is { Count: > 0 } inGame)
        {
            Console.Error.WriteLine(
                $"error: {string.Join(", ", inGame)} est connecté en jeu et réécrirait ces fichiers. "
                + "Reviens à l'écran de sélection de personnage, puis relance.");
            return 2;
        }

        // A bulk rewrite of dozens of files gets a copy aside first, whatever the settings say.
        if (apply)
        {
            string backup = MacroLibrary.BackupCharacter(
                character, _settings.BackupFolder ?? SettingsStore.DefaultBackupFolder, log: _log);
            Console.WriteLine($"sauvegarde : {backup}\n");
        }

        int lines = 0, files = 0, fixedTypos = 0;
        var touched = new List<MacroSetInfo>();

        foreach (var book in character.Books)
        {
            foreach (var set in book.Sets)
            {
                if (!set.Exists || !set.HasExpectedSize)
                    continue;

                MacroBook loaded;
                try
                {
                    loaded = set.Load();
                }
                catch (MacroFileException ex)
                {
                    Console.Error.WriteLine($"warning: {set.FileName} illisible ({ex.Message}).");
                    continue;
                }

                var suggestions = MacroRepair.Inspect(loaded)
                    .Where(s => s.Before.StartsWith("{00}", StringComparison.Ordinal))
                    .ToList();
                if (suggestions.Count == 0)
                    continue;

                files++;
                var repaired = new List<(MacroRepairSuggestion Suggestion, string Text)>();
                foreach (var suggestion in suggestions)
                {
                    string text = suggestion.After;
                    foreach (var (search, replacement) in substitutions)
                        text = MacroRepair.Substitute(text, search, replacement);

                    lines++;
                    if (text != suggestion.After)
                        fixedTypos++;

                    repaired.Add((suggestion, text));
                    Console.WriteLine($"  Book {book.Number,2} « {book.Title,-12} » Set {set.SetNumber,2}  "
                        + $"{suggestion.Where,-20}  {text}{(text != suggestion.After ? "   (faute corrigée)" : "")}");
                }

                if (!apply)
                    continue;

                foreach (var (suggestion, text) in repaired)
                {
                    if (suggestion.LineIndex < 0)
                        loaded.Macros[suggestion.MacroIndex].Name = text;
                    else
                        loaded.Macros[suggestion.MacroIndex].Lines[suggestion.LineIndex] = text;
                }

                set.Save(loaded);
                touched.Add(set);
            }
        }

        Console.WriteLine();
        if (lines == 0)
        {
            Console.WriteLine($"{character.Label} : aucune ligne cassée.");
            return 0;
        }

        if (apply)
        {
            Console.WriteLine($"{lines} ligne(s) réparée(s) dans {touched.Count} fichier(s)"
                + (fixedTypos > 0 ? $", dont {fixedTypos} avec une faute corrigée" : "") + ".");
            Console.WriteLine("Ces lignes vont maintenant s'exécuter en jeu — vérifie-les avant de les utiliser.");
        }
        else
        {
            Console.WriteLine($"{lines} ligne(s) cassée(s) dans {files} fichier(s)"
                + (fixedTypos > 0 ? $", dont {fixedTypos} avec une faute corrigée" : "") + ". Rien n'a été écrit.");
            Console.WriteLine($"Pour appliquer :  ffximacro repair {args[1]} --apply");
        }

        return 0;
    }

    private static MacroLibrary? OpenLibrary(string? path)
    {
        path ??= _settings.UserFolder;

        if (path is null)
        {
            var best = UserFolderLocator.DetectBest(log: _log);
            if (best is null)
            {
                Console.Error.WriteLine("error: no USER folder configured or detected. Run 'ffximacro find'.");
                return null;
            }

            Console.Error.WriteLine($"note: using auto-detected {best.Path} (make it permanent with 'config').");
            path = best.Path;
        }

        return MacroLibrary.Scan(path, _settings, _log);
    }

    /// <summary>Resolves a folder id, a readable name or a full path to a character.</summary>
    private static CharacterFolder? FindCharacter(string reference)
    {
        if (Directory.Exists(reference) && CharacterFolder.LooksLikeCharacterFolder(reference))
            return CharacterFolder.Scan(reference, _log);

        var library = OpenLibrary(null);
        if (library is null)
            return null;

        var match = library.ById(reference)
            ?? library.Characters.FirstOrDefault(c =>
                string.Equals(c.DisplayName, reference, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            Console.Error.WriteLine($"error: no character '{reference}' in {library.UserFolder}.");
            Console.Error.WriteLine($"       known: {string.Join(", ", library.Characters.Select(c => c.Id))}");
        }

        return match;
    }

    // ---------------------------------------------------------------- files

    private static int Show(string[] args)
    {
        if (args.Length < 2)
            return Fail("show needs a file path.");

        var book = MacroBookFile.Load(args[1]);

        Console.WriteLine($"{book.SourcePath}");
        Console.WriteLine($"  version 0x{book.Version:X16}   digest {(book.DigestWasValid ? "ok" : "MISMATCH")}");
        if (MacroFileNaming.TryParseFileName(args[1], out int index))
            Console.WriteLine($"  {MacroFileNaming.Describe(index)}");
        Console.WriteLine();

        for (int i = 0; i < MacroBook.MacroCount; i++)
        {
            var macro = book.Macros[i];
            if (macro.IsEmpty)
                continue;

            Console.WriteLine($"  [{MacroSlot.Describe(i),-6}] {macro.Name}");
            foreach (string line in macro.Lines.Where(l => l.Length > 0))
                Console.WriteLine($"            {line}");
        }

        if (book.IsEmpty)
            Console.WriteLine("  (no macros defined)");

        return 0;
    }

    private static int Dump(string[] args)
    {
        if (args.Length < 2)
            return Fail("dump needs a file path.");

        byte[] bytes = LongPath.ReadAllBytes(args[1]);
        int offset = args.Length > 2 ? ParseInt(args[2]) : 0;
        int length = args.Length > 3 ? ParseInt(args[3]) : Math.Min(bytes.Length - offset, 512);

        Console.Write(HexDump.Format(bytes, offset, length));
        return 0;
    }

    private static int Diff(string[] args)
    {
        if (args.Length < 3)
            return Fail("diff needs two file paths.");

        byte[] a = LongPath.ReadAllBytes(args[1]);
        byte[] b = LongPath.ReadAllBytes(args[2]);

        Console.Write(HexDump.Diff(a, b, Path.GetFileName(args[1]), Path.GetFileName(args[2])));
        return HexDump.DiffOffsets(a, b).Count == 0 ? 0 : 1;
    }

    private static int Titles(string[] args)
    {
        if (args.Length < 2)
            return Fail("titles needs a file path.");

        var set = BookTitleSet.Load(args[1]);
        Console.WriteLine($"{set.SourcePath}   digest {(set.DigestWasValid ? "ok" : "MISMATCH")}");
        for (int i = 0; i < BookTitleSet.TitleCount; i++)
            Console.WriteLine($"  Book {set.BookNumberAt(i),2}  {set.Titles[i]}");

        return 0;
    }

    private static int Verify(string[] args)
    {
        string target = args.Length > 1 ? args[1] : _settings.UserFolder ?? "";
        if (string.IsNullOrEmpty(target))
            return Fail("verify needs a file or folder path (or a configured USER folder).");

        var files = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, MacroFileNaming.SearchPattern, SearchOption.AllDirectories)
                .Where(p => MacroFileNaming.TryParseFileName(p, out _))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [target];

        int ok = 0, bad = 0;
        foreach (string path in files)
        {
            byte[] original = LongPath.ReadAllBytes(path);
            try
            {
                byte[] rewritten = MacroBookFile.ToBytes(MacroBookFile.Read(original));
                if (original.AsSpan().SequenceEqual(rewritten))
                {
                    ok++;
                }
                else
                {
                    bad++;
                    Console.WriteLine($"MISMATCH {path}");
                    Console.Write(HexDump.Diff(original, rewritten, "orig", "new", 8));
                }
            }
            catch (MacroFileException ex)
            {
                bad++;
                Console.WriteLine($"FAILED   {path}: {ex.Message}");
            }
        }

        Console.WriteLine($"\n{ok} file(s) round-tripped byte-for-byte, {bad} failure(s).");
        return bad == 0 ? 0 : 1;
    }

    private static int ParseInt(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(text, CultureInfo.InvariantCulture);
}
