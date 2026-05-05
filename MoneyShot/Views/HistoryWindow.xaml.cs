using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoneyShot.Models;
using MoneyShot.Services;

namespace MoneyShot.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryService _history;
    private readonly SaveService _saveService = new();

    public HistoryWindow(HistoryService history)
    {
        InitializeComponent();
        _history = history;
        Refresh();
    }

    private void Refresh()
    {
        var entries = _history.List();
        CountLabel.Text = entries.Count == 0
            ? "No captures yet — your saved screenshots will appear here."
            : $"{entries.Count} capture{(entries.Count == 1 ? string.Empty : "s")}";

        HistoryList.Items.Clear();
        foreach (var entry in entries)
        {
            HistoryList.Items.Add(BuildThumbnailTile(entry));
        }
    }

    private FrameworkElement BuildThumbnailTile(HistoryEntry entry)
    {
        var thumb = _history.LoadThumbnail(entry) ?? _history.LoadImage(entry);
        var image = new Image
        {
            Source = thumb,
            Width = 220,
            Height = 140,
            Stretch = Stretch.Uniform,
        };

        var meta = new TextBlock
        {
            Text = $"{entry.CapturedAt:yyyy-MM-dd HH:mm:ss}\n{entry.Width}×{entry.Height} · {entry.Source}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        var stack = new StackPanel { Width = 220 };
        stack.Children.Add(image);
        stack.Children.Add(meta);

        var border = new Border
        {
            Style = (Style)Resources["ThumbBorder"],
            Cursor = Cursors.Hand,
            Tag = entry,
            Child = stack,
        };
        border.MouseLeftButtonUp += Tile_OpenInEditor;
        border.ContextMenu = BuildContextMenu(entry);
        return border;
    }

    private ContextMenu BuildContextMenu(HistoryEntry entry)
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "Open in Editor" };
        openItem.Click += (_, _) => OpenInEditor(entry);
        menu.Items.Add(openItem);

        var copyItem = new MenuItem { Header = "Copy to Clipboard" };
        copyItem.Click += (_, _) => CopyToClipboard(entry);
        menu.Items.Add(copyItem);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) =>
        {
            if (MessageBox.Show("Delete this capture from history?", "Money Shot",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _history.Delete(entry);
            Refresh();
        };
        menu.Items.Add(deleteItem);

        return menu;
    }

    private void Tile_OpenInEditor(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is HistoryEntry entry) OpenInEditor(entry);
    }

    private void OpenInEditor(HistoryEntry entry)
    {
        var image = _history.LoadImage(entry);
        if (image == null)
        {
            MessageBox.Show("This capture's image file could not be loaded.", "Money Shot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var editor = new EditorWindow(image);
        editor.ShowDialog();
    }

    private void CopyToClipboard(HistoryEntry entry)
    {
        var image = _history.LoadImage(entry);
        if (image == null) return;
        try
        {
            _saveService.SaveToClipboard(image);
        }
        catch (Exception ex)
        {
            Logger.Error("Copy from history failed", ex);
            MessageBox.Show("Failed to copy image to clipboard.", "Money Shot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _history.HistoryDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open history folder", ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
