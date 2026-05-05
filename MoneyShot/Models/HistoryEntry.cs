namespace MoneyShot.Models;

/// <summary>
/// Sidecar metadata for a saved history capture. Persisted as a .json file beside the .png.
/// </summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Source { get; set; } = string.Empty; // "FullScreen", "Region", "Monitor 1", etc.
    public string ImageFileName { get; set; } = string.Empty;
    public string ThumbnailFileName { get; set; } = string.Empty;
}
