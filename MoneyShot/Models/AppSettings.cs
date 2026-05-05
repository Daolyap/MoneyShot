using System.Windows;
using System.Windows.Media;

namespace MoneyShot.Models;

public class AppSettings
{
    public SaveDestination DefaultSaveDestination { get; set; } = SaveDestination.Both;
    public string DefaultSavePath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string DefaultFileFormat { get; set; } = "PNG";
    public bool RunOnStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartInTray { get; set; } = true;
    public bool DisableWindowsPrintScreen { get; set; } = false;
    public bool HideUiFromScreenshots { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public Color DefaultAnnotationColor { get; set; } = Colors.Red;
    public int DefaultLineThickness { get; set; } = 3;
    public string HotKeyCapture { get; set; } = "PrintScreen";
    public string HotKeyRegionCapture { get; set; } = "Ctrl+PrintScreen";
}
