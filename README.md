# FFXI Macro Editor

**English** · [Français](README.fr.md)

Your 800 macros, on one screen. Edit them like a text editor, not like a menu.

**[Download for Windows →](https://github.com/ejouanchicot/ffxi-macro-editor/releases/latest)**
One file, 27 MB, nothing to install.

---

## What it's for

Final Fantasy XI gives you 40 books × 10 sets × 20 macros. In game you reach them through a menu,
six lines at a time, one macro at a time. Setting up a new job is an evening. Renaming a spell
across a whole book is worse.

This opens the lot in a window: pick a book on the left, the twenty macros of the set are laid out
exactly like the game's `Ctrl` and `Alt` rows, and the one you click opens in an editor with its six
lines. Type, save, done.

## What you get

**Auto-translate phrases in plain text.** In the raw files, `Provoke` is six bytes of binary. Here it
reads `«Provoke»`, and `Ctrl+Space` opens a search: type `mighty`, click `Mighty Strikes`, it lands
at your cursor. The game renders it in its usual brackets, in whatever language the client runs. No
other tool needed — the names are read from your own FFXI installation.

**Edit while you play.** The game only holds on to the book currently on screen. Every other book is
read from disk the moment you switch to it, so you can rewrite them mid-session and the change is
live as soon as you switch book or change job. No restart, no relog.

**Search everything at once.** One field, every book of every character on the machine: command
lines, macro names, book titles. Each result says exactly where it is, and a click takes you there.
Worth its weight the day an addon renames a command.

**Move things around.** Drag a macro onto another to swap them, `Ctrl`-drag to copy. Drag a whole
book onto another to move its ten sets and its title along with it — across characters too. A book
copy asks first, because it overwrites ten files.

**Export a set** to plain text or JSON: keep it in version control, send it to a friend, import it
back. It round-trips exactly, gaps and trailing spaces included.

**Repair what's broken.** A macro that lost its leading `/` at some point does nothing in game and
still looks fine in the menu. The editor finds those and puts the slash back.

## Getting started

1. [Download `FfxiMacroEditor.exe`](https://github.com/ejouanchicot/ffxi-macro-editor/releases/latest)
   and run it. It is self-contained — no .NET, no runtime, no installer.
2. Windows will say **“Windows protected your PC”**, because the file is not code-signed.
   *More info → Run anyway.* The release page publishes a SHA-256 if you want to check your download.
3. It finds your `USER` folder on its own, PlayOnline and Steam installs alike. If it guesses wrong,
   **USER folder…** at the bottom left sets it, and it is remembered.

Shortcuts: `Ctrl+S` save, `Ctrl+Shift+S` save everything, `F5` put a set back the way it is on disk,
`Ctrl+PageUp` / `Ctrl+PageDown` to walk the sets, `Ctrl+Space` for phrases.

The interface is in **English or French** — the `EN` / `FR` buttons in the header, no restart.

## About your files

Your macros are the record of a lot of evenings, so:

- **Every set is copied before the first write** of a session, into
  `%APPDATA%\FfxiMacroEditor\Backups\`. Nothing is overwritten without a copy sitting beside it.
- **What it writes back is what the game wrote**, byte for byte. Anything it does not understand is
  carried through untouched rather than dropped. That guarantee is re-checked against 493 real files
  on every build, and it is the reason this project exists.
- **Nothing leaves your machine.** No account, no telemetry, no network. It reads and writes files in
  your own FFXI folder, and that is all it does.
- The one thing to know: **the book currently open in game will be overwritten by the client** if you
  save it. The editor shows a banner while a character is logged in, and names them. Every other book
  is yours to edit.

## For the curious

The macro file format is not documented anywhere, so it was worked out from real files and written
down: [the binary format, as observed](docs/FORMAT.md) — the 24-byte header, the 380 bytes per macro,
how auto-translate phrases are stored inside a line, and how their names are pulled out of the game's
own data tables.

Built with C# and [Avalonia](https://avaloniaui.net/). The library that reads and writes the files
has no UI dependency and stands on its own; there is a command-line tool as well.

```bash
dotnet build FfxiMacroEditor.sln
dotnet test  FfxiMacroEditor.sln     # 337 tests
```

---

## License

[MIT](LICENSE).

The character names and folder ids used in the documentation and in the test samples (`Kaelith`,
`Sylvane`, `a1b2c3d`) are placeholders.

This project is not affiliated with or endorsed by Square Enix. It reads and writes the macro files
of a legally installed copy of the game, on your own machine. No game data is redistributed: the
auto-translate names are read at runtime from the installation you already have.
