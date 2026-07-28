using System.Globalization;

namespace FfxiMacros.Core.Diagnostics;

public enum MacroLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Minimal logging seam. The original tool swallowed its errors; every skipped file or failed probe
/// in this one goes through here so <c>--debug</c> can surface it.
/// </summary>
public interface IMacroLog
{
    void Write(MacroLogLevel level, string message);
}

public static class MacroLogExtensions
{
    public static void Debug(this IMacroLog? log, string message) => log?.Write(MacroLogLevel.Debug, message);

    public static void Info(this IMacroLog? log, string message) => log?.Write(MacroLogLevel.Info, message);

    public static void Warn(this IMacroLog? log, string message) => log?.Write(MacroLogLevel.Warning, message);

    public static void Error(this IMacroLog? log, string message) => log?.Write(MacroLogLevel.Error, message);
}

/// <summary>Routes log lines to a callback — a console writer, or the UI log pane later on.</summary>
public sealed class DelegateLog(Action<MacroLogLevel, string> sink, MacroLogLevel minimum = MacroLogLevel.Info) : IMacroLog
{
    public void Write(MacroLogLevel level, string message)
    {
        if (level >= minimum)
            sink(level, message);
    }
}

/// <summary>Appends log lines to a file. Failures to log are never allowed to break the app.</summary>
public sealed class FileLog : IMacroLog, IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly MacroLogLevel _minimum;
    private readonly object _gate = new();

    public FileLog(string path, MacroLogLevel minimum = MacroLogLevel.Debug)
    {
        _minimum = minimum;
        Path = path;
        try
        {
            string? folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writer = null;
            OpenError = ex.Message;
        }
    }

    public string Path { get; }

    /// <summary>Set when the log file could not be opened; the app keeps running without a log.</summary>
    public string? OpenError { get; }

    public void Write(MacroLogLevel level, string message)
    {
        if (_writer is null || level < _minimum)
            return;

        lock (_gate)
        {
            try
            {
                _writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-7} {message}"));
            }
            catch (IOException)
            {
                // A broken log must never take the editor down with it.
            }
        }
    }

    public void Dispose() => _writer?.Dispose();
}

/// <summary>Sends log lines to several sinks at once (file plus console, say).</summary>
public sealed class CompositeLog(params IMacroLog[] sinks) : IMacroLog
{
    public void Write(MacroLogLevel level, string message)
    {
        foreach (var sink in sinks)
            sink.Write(level, message);
    }
}
