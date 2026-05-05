using System.Globalization;
using System.IO;

namespace MoneyShot.Services;

/// <summary>
/// Lightweight logging facade. Writes to Debug.WriteLine (so existing dev workflows are
/// unaffected) AND to a daily rolling file at %AppData%\MoneyShot\logs\moneyshot-YYYYMMDD.log
/// so Release builds also leave a diagnostic trail. Files older than RetentionDays are pruned
/// on first call after a process starts.
///
/// Note: this deliberately replaces the original idea of bringing in Microsoft.Extensions.Logging
/// + DI throughout. The actual gap was "Debug.WriteLine produces no Release output", and a
/// 100-line static facade closes that gap without rewriting every service's constructor.
/// </summary>
internal static class Logger
{
    private const int RetentionDays = 7;
    private const string FileNameFormat = "moneyshot-{0:yyyyMMdd}.log";

    private static readonly object FileLock = new();
    private static readonly Lazy<string> LogDirectory = new(InitialiseLogDirectory);
    private static bool _retentionEnforced;

    public static string LogDirectoryPath => LogDirectory.Value;

    public static void Debug(string message) => Write("DBG", message, exception: null);
    public static void Info(string message) => Write("INF", message, exception: null);
    public static void Warn(string message, Exception? exception = null) => Write("WRN", message, exception);
    public static void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = exception == null
            ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}"
            : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message} :: {exception.GetType().Name}: {exception.Message}";

        System.Diagnostics.Debug.WriteLine(line);

        try
        {
            EnsureRetentionEnforced();
            var path = Path.Combine(LogDirectory.Value, string.Format(CultureInfo.InvariantCulture, FileNameFormat, DateTime.Now));
            lock (FileLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never throw into caller code. Swallow filesystem errors silently;
            // the Debug.WriteLine above is still useful in dev.
        }
    }

    private static string InitialiseLogDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoneyShot",
            "logs");
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            // Fallback to temp if AppData isn't writable for some reason. Better than crashing.
            dir = Path.Combine(Path.GetTempPath(), "MoneyShot-logs");
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private static void EnsureRetentionEnforced()
    {
        if (_retentionEnforced) return;
        _retentionEnforced = true;
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory.Value, "moneyshot-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort cleanup; ignore failures.
        }
    }
}
