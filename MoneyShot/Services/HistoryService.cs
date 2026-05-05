using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;
using MoneyShot.Models;

namespace MoneyShot.Services;

/// <summary>
/// Persists capture history to %AppData%\MoneyShot\history. Each capture becomes three files:
/// {id}.png (full-resolution image), {id}-thumb.png (downscaled preview), and {id}.json (metadata).
/// History is local-only by design — never uploaded anywhere.
/// </summary>
public sealed class HistoryService
{
    private const int ThumbnailMaxWidth = 400;
    private const int DefaultRetentionCount = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _historyDirectory;

    public HistoryService()
    {
        _historyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoneyShot",
            "history");
        try
        {
            Directory.CreateDirectory(_historyDirectory);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not create history directory", ex);
        }
    }

    public string HistoryDirectory => _historyDirectory;

    /// <summary>
    /// Saves a capture to history and trims the oldest entries to honour <paramref name="retentionCount"/>.
    /// Failures are logged but never throw — losing a history entry should not break the capture flow.
    /// </summary>
    public HistoryEntry? Save(BitmapSource image, string source, int retentionCount = DefaultRetentionCount)
    {
        try
        {
            var id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            var entry = new HistoryEntry
            {
                Id = id,
                CapturedAt = DateTime.Now,
                Width = image.PixelWidth,
                Height = image.PixelHeight,
                Source = source,
                ImageFileName = $"{id}.png",
                ThumbnailFileName = $"{id}-thumb.png",
            };

            EncodePngToFile(image, Path.Combine(_historyDirectory, entry.ImageFileName));
            EncodePngToFile(CreateThumbnail(image), Path.Combine(_historyDirectory, entry.ThumbnailFileName));
            File.WriteAllText(
                Path.Combine(_historyDirectory, $"{id}.json"),
                JsonSerializer.Serialize(entry, JsonOptions));

            EnforceRetention(retentionCount);
            return entry;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save history entry", ex);
            return null;
        }
    }

    public IReadOnlyList<HistoryEntry> List()
    {
        try
        {
            return Directory.EnumerateFiles(_historyDirectory, "*.json")
                .Select(LoadEntry)
                .Where(e => e != null)
                .Select(e => e!)
                .OrderByDescending(e => e.CapturedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to list history", ex);
            return Array.Empty<HistoryEntry>();
        }
    }

    public BitmapSource? LoadImage(HistoryEntry entry) =>
        LoadPng(Path.Combine(_historyDirectory, entry.ImageFileName));

    public BitmapSource? LoadThumbnail(HistoryEntry entry) =>
        LoadPng(Path.Combine(_historyDirectory, entry.ThumbnailFileName));

    public void Delete(HistoryEntry entry)
    {
        TryDelete(Path.Combine(_historyDirectory, entry.ImageFileName));
        TryDelete(Path.Combine(_historyDirectory, entry.ThumbnailFileName));
        TryDelete(Path.Combine(_historyDirectory, $"{entry.Id}.json"));
    }

    private void EnforceRetention(int retentionCount)
    {
        if (retentionCount <= 0) return;
        var entries = List();
        if (entries.Count <= retentionCount) return;
        foreach (var stale in entries.Skip(retentionCount))
        {
            Delete(stale);
        }
    }

    private static HistoryEntry? LoadEntry(string jsonPath)
    {
        try
        {
            return JsonSerializer.Deserialize<HistoryEntry>(File.ReadAllText(jsonPath));
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? LoadPng(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            // Cache OnLoad + close stream so the file isn't held open (lets Delete succeed later).
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load history image at {path}", ex);
            return null;
        }
    }

    private static BitmapSource CreateThumbnail(BitmapSource source)
    {
        if (source.PixelWidth <= ThumbnailMaxWidth) return source;
        var scale = (double)ThumbnailMaxWidth / source.PixelWidth;
        var scaled = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private static void EncodePngToFile(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
