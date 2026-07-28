using FfxiMacros.Core.Io;
using FfxiMacros.Core.Model;

namespace FfxiMacros.Core.Discovery;

/// <summary>One <c>mcr*.dat</c> slot of a character: a set of 20 macros, present on disk or not.</summary>
public sealed class MacroSetInfo
{
    public MacroSetInfo(int fileIndex, string folder)
    {
        FileIndex = fileIndex;
        FileName = MacroFileNaming.FileName(fileIndex);
        FullPath = Path.Combine(folder, FileName);
    }

    /// <summary>Raw index 0..399.</summary>
    public int FileIndex { get; }

    /// <summary>1-based book number, 1..40.</summary>
    public int BookNumber => MacroFileNaming.BookOf(FileIndex);

    /// <summary>1-based set number within the book, 1..10.</summary>
    public int SetNumber => MacroFileNaming.SetOf(FileIndex);

    public string FileName { get; }

    public string FullPath { get; }

    public bool Exists { get; internal set; }

    public DateTime LastWriteUtc { get; internal set; }

    public long SizeBytes { get; internal set; }

    /// <summary>False when the file exists but is not 7624 bytes; loading it will fail loudly.</summary>
    public bool HasExpectedSize => !Exists || SizeBytes == MacroBookFile.FileSize;

    public MacroBook Load() => MacroBookFile.Load(FullPath);

    public void Save(MacroBook book, bool truncate = false)
    {
        MacroBookFile.Save(book, FullPath, truncate);
        Refresh();
    }

    /// <summary>Re-reads presence, size and timestamp from disk — after a save, or an external change.</summary>
    public void Refresh()
    {
        var file = new FileInfo(LongPath.Normalize(FullPath));
        Exists = file.Exists;
        SizeBytes = file.Exists ? file.Length : 0;
        LastWriteUtc = file.Exists ? file.LastWriteTimeUtc : default;
    }

    public override string ToString() => $"{FileName} (book {BookNumber}, set {SetNumber})";
}
