# The FFXI macro format, as observed

[English](FORMAT.md) · [Français](FORMAT.fr.md) — back to the [README](../README.md)

Everything here was worked out from a real installation and checked against real files, not guessed.
It is the reference behind the editor's one hard promise: what it writes back is what the game
wrote, to the byte.

# Binary format — verified against real data

Everything below was confirmed on 493 files from 5 characters, not merely inferred from
decompilation. The ⚠️ points correct the original spec.

## Common header (24 bytes)

Shared by `mcr*.dat`, `mcr*.ttl` and `mcr.sys`:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 8 | Version / stamp. Low word always `1`, high word varies by install. **Copied through as-is.** |
| 8 | 16 | MD5 of everything that follows. **Recomputed on every write.** |

The stored MD5 matched the data in **503 files out of 503**.

## Macro file `mcr<N>.dat` — 7624 bytes

24 bytes of header + 7600 bytes of data = 20 macros × 380 bytes.

Each macro (380 bytes):

| Offset | Size | Field | Observed |
|---:|---:|---|---|
| 0 | 4 | reserved / flags | `00 00 00 00` in all 9860 macros read → copied through |
| 4 | 61 | line 1 | text + `0x00` padding, **60 usable bytes max** (the 61st is always null) |
| 65…309 | 61×5 | lines 2 to 6 | same |
| 370 | 9 | name | text + padding, **8 usable bytes max** |
| 379 | 1 | reserved | `0x00` everywhere → copied through |

Lengths actually observed: longest line 55 bytes, longest name 8 bytes.

## ⚠️ 40 books × 10 sets, not 20 books

The spec announced “20 books per character”. The reality, confirmed by a folder holding all 400
files:

- a character folder holds up to **400 files**: `mcr.dat` (index 0) then `mcr1.dat` … `mcr399.dat`;
- `index = (book − 1) × 10 + (set − 1)` → **40 books of 10 sets of 20 macros**;
- the files are created on demand by the game, so most folders hold far fewer (here: 400, 66, 14, 12
  and 1);
- independent check: the modification dates cluster in tens (140-149, 190-199) and the title files
  hold exactly 40 names in total.

## ⚠️ Titles: two `.ttl` files of 20 titles

The spec spoke of “an array of ~10 names”. In reality:

- `mcr.ttl` → books 1-20, `mcr_2.ttl` → books 21-40;
- 344 bytes = 24 of header + **20 fields of 16 bytes** (15 usable);
- the game writes `Book01` … `Book40` for an untitled book.

`mcr.sys` (28 bytes = header + 4 bytes of data) is not interpreted yet — it is left untouched.

## ⚠️ Auto-translate: present since v1, never lost

Auto-translate phrases are stored **inside** the lines, as `FD b1 b2 b3 b4 FD` (6 bytes) — 422
occurrences in the corpus, e.g. `/ja "<FD 02 02 1F 97 FD>" <t>`. A plain ASCII passthrough would
destroy them on save.

`FfxiText` therefore uses a **lossless** text form, from milestone 1 onwards:

| Editable form | Bytes on disk |
|---|---|
| `/ja "Provoke" <t>` | ASCII 0x20-0x7E |
| `«02021F97»` or `«Provoke»` | `FD 02 02 1F 97 FD` |
| `{00}`, `{9E}`, … | any other byte |
| `{{` | a literal `{` |

### Readable phrase names (delivered ahead of milestone 5)

The exact structure, confirmed on the 108 distinct sequences in the corpus, is
`FD <table> 02 <16-bit id, big-endian> FD`:

| `<table>` | Contents | Sequences in the corpus |
|---|---|---|
| `0x02` | auto-translate list (Provoke, Savage Blade, Haste Samba…) | 105 |
| `0x07` | item list (Forbidden Key, Panacea, Foil) | 3 |

The names come **from the game's own files** (see milestone 5 below). A **Windower** install, if
present, fills in what the client keeps as markers (place and job names) and supplies the items.
With neither, everything stays as `«02021F01»` — less readable, never wrong.

| Editable form | Meaning |
|---|---|
| `«Provoke»` | auto-translate phrase |
| `«item Forbidden Key»` | item |
| `«Vallation#1FF2»` | a name the game reuses (here ids 8156 and 8178): the id is spelled out to remove the ambiguity |
| `«02021F01»` | phrase the dictionary does not know |

The guillemets echo the brackets the game draws around an auto-translate phrase. **The game never
sees this notation**: it is the editor's display only, and saving writes back the original 6 bytes
`FD 02 02 xx xx FD`. The `{AT:Provoke}` form is still accepted as input, for anyone without `«` at
hand.

A reused name is not necessarily reused only twice: “Animated Flourish” covers ids 8094, 8095 and
8117. Writing the id on every one of them filled macros with `«Animated Flourish#1F9E»` for nothing —
**the first id therefore keeps the bare name** (it is the one the game's own menu inserts), and only
the later ones carry their id, because that is the only way to preserve their bytes.

**The exactness guarantee does not depend on the dictionary**: a name is only written when
re-encoding it reproduces exactly the same bytes — otherwise the hexadecimal form wins. The
round-trip over the 493 real files stays identical byte for byte, dictionary loaded or not.

## ⚠️ 52 corrupted lines

In the corpus, 52 lines of character `a1b2c3d` have their leading `/` **replaced by a `0x00` byte**
(`{00}con send Kaelith "Healing Waltz" <laststid>`), sometimes followed by the remains of a longer
line after the terminator. The game stops at the first `0x00`: **those lines do nothing in game**.

A naive “cut at the first `0x00`” decode would silently lose the rest of the line on save. The
decoder therefore keeps the internal null bytes and shows them as `{00}` — visible, repairable, and
the round-trip stays exact.

**The interface shows everywhere what the game shows**, names as well as lines: a field stored
`Palis{00}el` reads as `Palis`, and `/ta <stpc>{00}" <t>` reads as `/ta <stpc>`. Dead bytes are not
displayed, and **they are removed from the file on the set's first save** — the game was not reading
them, so dropping them changes nothing in game, but the file stops carrying an older tool's
leftovers.

One deliberate exception: a field whose **first** byte is null (`{00}con send …`) is never cleaned
automatically. The game runs nothing there, so there is no debris to remove — only recoverable text,
and deleting it silently would lose it. The **Repair** button is what restores it, by putting the
leading `/` back: that operation changes what the game executes, so it stays manual.

---

# Disk discovery (milestone 2)

## Detecting the `USER` folder

No path is hard-coded. `UserFolderLocator.Detect()` probes, in order:

1. the `FFXI_USER_DIR` environment variable, then the folder remembered in `settings.json`;
2. **PlayOnline** installs: `PlayOnline\SquareEnix\FINAL FANTASY XI\USER` and the
   `PlayOnlineViewer\…` variant, under `Program Files`, `Program Files (x86)` and at the root of
   every fixed drive;
3. **Steam** installs: reading `steamapps\libraryfolders.vdf` (with a hand-written KeyValues parser)
   to enumerate every library, then looking for `SquareEnix\FINAL FANTASY XI\USER` inside **each**
   folder of `steamapps\common` — not only inside `FFXIPAL`, because that folder is sometimes
   renamed;
4. a surface sweep of the fixed drives (`<drive>\…`, `<drive>\Games\…`, `<drive>\FFXIPAL\…`).

Candidates are ranked by character count, then by activity date. Nothing throws: an unreadable drive
or a truncated `.vdf` is logged and skipped.

On the development machine, detection finds the real Steam install **and** a copy of the game folder
— which is why the whole of `steamapps\common` is swept:

```
D:\Steam\steamapps\common\FFXIPAL\SquareEnix\FINAL FANTASY XI\USER          5 characters
D:\Steam\steamapps\common\FFXIPAL - Copie\SquareEnix\FINAL FANTASY XI\USER  1 character
```

`Resolve()` is forgiving about what the user picks in a folder browser: the `USER` folder, the game
folder above it, or even a character folder — all of them lead back to the right `USER`.

## Book/set mapping confirmed on real data

Listing a played character confirms `index = (book−1)×10 + (set−1)`: books actually in use have all
10 sets, the others have only the first.

```
Book  1  RdmBlm   sets [1234567890]
Book  3  CorDnc   sets [12345..890]
Book  7  Book07   sets [1.........]
```

## Persistent settings

`%APPDATA%\FfxiMacroEditor\settings.json`: the current `USER` folder, recent folders, the
`hex id → readable name` mapping, the backup folder, log options, interface language. A corrupted
file is never fatal: it is reported and replaced on the next write.

## Logging

`IMacroLog` (file, console, or both) replaces the swallowed errors of the old tool. Logged: every
`USER` candidate found, every Steam library, every skipped file and why, every file of abnormal
size, every unreadable `.ttl`. `--debug` writes
`%APPDATA%\FfxiMacroEditor\ffxi-macro-editor.log`.

## What the scan skips, and says so

- `mcr*.dat` files that do not follow the game's naming (`mcrx.dat`, `mcr07.dat`) → listed in
  `CharacterFolder.SkippedFiles` and logged as “not mcr#.dat”;
- macro files whose size is not 7624 bytes → reported, flagged `HasExpectedSize = false`, never
  loaded silently;
- subfolders of `USER` with no macro file;
- a character folder with a non-hexadecimal name is **kept** (with a note) rather than rejected.

## Backups

`MacroLibrary.BackupCharacter` copies only `mcr*.dat`, `mcr*.ttl` and `mcr.sys` into
`Backups\<id>-<timestamp>\` — never the whole folder, which holds megabytes of unrelated game data.

## Guard rails implemented

- refuses to read a file that is not 7624 bytes (or not 344 for a `.ttl`), with an explicit message;
- refuses to write when the data block is not 7600 bytes;
- refuses to write a line over 60 bytes or a name over 8 bytes — or truncates cleanly on request,
  never cutting an auto-translate phrase in half;
- atomic writes (temporary file, then replace);
- Windows long paths handled through the `\\?\` prefix, with a real error if it still fails;
- no exotic Windows dependency (no “East Asian language support”).

---

# Advanced editing (milestone 4)

## Search

The **Search…** field sweeps the whole `USER` folder: command lines, macro names and book titles,
case-insensitively. Each result gives its exact position
(`Kaelith · Book 15 “PldRunR” · Set 1 · Ctrl-2 · line 1`) and a click opens that macro directly. The
search stops at 500 results so a common word cannot drown the list.

## Copy / move

| Gesture | Effect |
|---|---|
| drag a macro onto another | **swaps** the two slots |
| `Ctrl` + drag a macro | **copies** onto the destination |
| right-click a macro | Copy / Paste / Clear (the clipboard crosses sets and characters) |
| drag a book onto another | **moves** the book (10 sets + title) |
| `Ctrl` + drag a book | **copies** the book |

A macro swaps rather than overwrites: nothing is lost to an unlucky gesture, and `F5` reloads the
set from disk anyway.

Moving a book overwrites ten files at once, so **it is never applied straight away**: a confirmation
bar states precisely what will happen (“… 3 set(s) of the destination book will be overwritten, and
the source book will be emptied”). Both characters involved are backed up before the write, and the
operation is refused while unsaved changes remain.

Titles follow: copying a book copies its title, moving it empties the source title, and the right
`.ttl` (`mcr.ttl` or `mcr_2.ttl`, depending on the book number) is rewritten.

## Import / export

**Export…** writes the current set as readable `.txt` or structured `.json`; **Import…** reads
either back into the current set, without saving until you click Save.

```
# FFXI macro set
# Kaelith (a1b2c3d) · book 15 (PldRunR) · set 1

[Ctrl-1] ShieldBa
/ja "«Shield Bash»" <stnpc>

[Ctrl-2] Flash
/ma "«Flash»" <stnpc>
```

Both formats round-trip to the byte, including the awkward cases: a macro that leaves line 2 empty
and uses line 3 keeps its positions, and a name whose trailing space matters (`"Box "`) is quoted so
it survives a text editor.

## Repair

**Repair** puts back the leading `/` that an older tool had replaced with a null byte — which wakes
up a line the game was ignoring, hence it being an explicit gesture. Plain line remains after the
terminator disappear on their own at the first save. Nothing is written until you save.

---

# Reading the game's tables (milestone 5)

The point of this milestone: stop depending on a third-party tool to display auto-translate phrases.
Everything below was reconstructed from a real installation, then checked against an independent
source.

## `VTABLE.DAT` + `FTABLE.DAT` — the data file index

The spec knew of only one constraint: “FTABLE is exactly 2× the size of VTABLE”. Verified
(219,402 / 109,701), and here is why:

- `VTABLE.DAT` — **one byte per id**: the ROM volume number, or 0 when the id is unused;
- `FTABLE.DAT` — **two bytes per id** (little-endian): the directory in the high 9 bits, the file in
  the low 7.

Hence `ROM<volume>/<packed >> 7>/<packed & 0x7F>.DAT`. On the test installation: 109,701 ids, of
which 83,116 are in use, and **all 83,116 point at a file that exists**.

## The auto-translate dictionary

It lives in `ROM/168/25.DAT`, and its format is self-describing:

```
02 02 <group> <index> <length> <text…> 00
```

The first two bytes are exactly the ones a macro stores between its `FD` markers, and **a phrase's
id is simply `(group << 8) | index`** — which confirms, from the game's own data, what had been
inferred from the macros at milestone 3. A record with index 0 opens a group: a fixed 76-byte block
carrying the category name (【Greetings】, 【Job Abilities】…).

Result of the parser on the real file: **2685 phrases, 42 groups, and the parse ends exactly on the
last byte of the file**.

## The client's markers

Many phrases are not stored in plain text but as a marker the client substitutes at runtime:

| Marker | Contents | Table | Resolved |
|---|---|---|---|
| `@Y<hex>` | abilities, traits, weapon skills, pet commands | `ROM/181/72.DAT` (5888 entries) | ✅ **713 / 713** |
| `@C<hex>` | spells, blue magic | `ROM/181/73.DAT` (1024 entries) | ✅ **311 / 311** |
| `@A<hex>` | place names | table not decoded | ❌ |
| `@J<hex>` | job names | table not decoded | ❌ |

These tables are in `d_msg` format: 64-byte header, fixed-size entries, text at +40. The marker is a
direct index: `@Y22E` = entry 0x22E = 558 = “Shield Bash”.

The two unresolved categories (259 phrases) are place and job names — you practically never put one
in a macro, and they fall back cleanly to the hexadecimal form. **Items** are not in a `d_msg` table
(different format) and stay covered by Windower.

## Validation

The decoding was checked against a completely independent source — Windower's resource files,
themselves extracted from the game by another tool:

- **1252 phrases identical** to the character;
- **1024 markers out of 1024** (`@Y` + `@C`) resolved to the expected name;
- **2 discrepancies**, both caused by quote escaping in my comparison script, not by the decoding.

## What holds up over time

The file ids (55665 for the dictionary, 55701 and 55702 for the name tables) are only a **starting
point**: every file is validated by its content. Should a game update move them, the loader sweeps
the installation and finds the right files again — the dictionary by its signature, the name tables
by scoring them on how well they resolve the markers actually present. Locating the 313 `d_msg`
tables of an installation takes about 5 seconds.

And if none of that works out, the editor shows `«02021F01»` and carries on — exactly the clean
fallback the spec asked for, without the original tool's crash.

---

# Editing while the game is running

**The client only holds the book shown on screen.** That is the rule, measured and then confirmed in
game: it reads a book's macros from disk the moment you switch to it, and keeps only the current one
in memory. Proof from the client's memory, logged in:

```
book 1  (ThfRdm)   con gs c smartbuff              absent from memory
book 36 (BrdDncC)  con sm all follow Kaelith       PRESENT   ← the displayed book
book 2  (ThfGeo)   con gs c cycle altPlayerLight   absent
```

Practical consequences:

- **editing a book you are not on → works live.** You save, you switch to it in game (or change job,
  which is what a macro changer does) and the change is active. Verified in game;
- **editing the displayed book → lost.** The client owns its copy and writes it back over yours.
  Observed: a line saved at 18:34 came back one second later with the old content and the client's
  version stamp.

The editor therefore shows a banner naming the connected characters and restating the rule, but no
longer blocks saving — only the book in front of your eyes is at stake.

**To edit the displayed book without closing the game, the character-select screen is enough.**
Verified stopwatch in hand:

```
18:50:29   the client writes mcr.ttl, mcr_2.ttl, mcr.sys   ← logging out: it flushes its macros
18:50:47   the editor writes mcr350.dat                     ← save, 18 s later
```

The added line was indeed in the file, and present in game on the next login. The editor recognises
that state, too: the FFXI window title is the character's name while in game and “Final Fantasy XI”
at the select screen, so the banner disappears as soon as you log out.

Editing macros **while actually playing** stays out of reach for an external editor: it would mean
writing into the client's memory, which is what Windower addons do. That is a project of a different
nature, and the original spec already placed real-time in-game editing out of scope.
