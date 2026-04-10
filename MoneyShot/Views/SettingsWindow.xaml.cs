using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using MoneyShot.Models;
using MoneyShot.Services;
using Application = System.Windows.Application;

namespace MoneyShot.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _settings = _settingsService.LoadSettings();
        LoadSettings();
        LoadMonitorHotkeysInfo();
    }

    private void LoadMonitorHotkeysInfo()
    {
        var screenshotService = new ScreenshotService();
        var screens = screenshotService.GetAllScreens();
        
        if (screens.Count > 1)
        {
            var hotkeys = new StringBuilder();
            for (int i = 0; i < Math.Min(screens.Count, 9); i++)
            {
                if (i > 0) hotkeys.Append(", ");
                hotkeys.Append($"Ctrl+Shift+{i + 1}");
            }
            MonitorHotkeysInfo.Text = $"Detected {screens.Count} monitor(s). Hotkeys: {hotkeys}";
        }
        else
        {
            MonitorHotkeysInfo.Text = "Only one monitor detected. Individual monitor hotkeys are not available.";
        }
    }

    private void LoadSettings()
    {
        StartInTrayCheckbox.IsChecked = _settings.StartInTray;
        RunOnStartupCheckbox.IsChecked = _settings.RunOnStartup;
        MinimizeToTrayCheckbox.IsChecked = _settings.MinimizeToTray;
        CheckForUpdatesCheckbox.IsChecked = _settings.CheckForUpdatesOnStartup;
        if (_settingsService.TryGetWindowsPrintScreenDisabled(out var isPrintScreenDisabled))
        {
            _settings.DisableWindowsPrintScreen = isPrintScreenDisabled;
        }

        DisableWindowsPrintScreenCheckbox.IsChecked = _settings.DisableWindowsPrintScreen;
        SavePathTextBox.Text = _settings.DefaultSavePath;
        
        SaveToClipboardRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Clipboard;
        SaveToFileRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.File;
        SaveToBothRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Both;

        FormatComboBox.SelectedItem = _settings.DefaultFileFormat;
        
        // Load hotkey settings
        SelectComboBoxItem(HotKeyCaptureComboBox, _settings.HotKeyCapture);
        SelectComboBoxItem(HotKeyRegionCaptureComboBox, _settings.HotKeyRegionCapture);
    }

    private void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in comboBox.Items)
        {
            if (item.Content.ToString() == value)
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.SelectedPath = _settings.DefaultSavePath;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Validate the selected path
                if (!string.IsNullOrWhiteSpace(dialog.SelectedPath) && Directory.Exists(dialog.SelectedPath))
                {
                    SavePathTextBox.Text = dialog.SelectedPath;
                }
                else
                {
                    MessageBox.Show("The selected folder is invalid.", "Invalid Folder", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error selecting folder: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.StartInTray = StartInTrayCheckbox.IsChecked ?? true;
            _settings.RunOnStartup = RunOnStartupCheckbox.IsChecked ?? false;
            _settings.MinimizeToTray = MinimizeToTrayCheckbox.IsChecked ?? false;
            _settings.CheckForUpdatesOnStartup = CheckForUpdatesCheckbox.IsChecked ?? true;
            _settings.DisableWindowsPrintScreen = DisableWindowsPrintScreenCheckbox.IsChecked ?? false;
            _settings.DefaultSavePath = SavePathTextBox.Text;

            if (SaveToClipboardRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.Clipboard;
            else if (SaveToFileRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.File;
            else
                _settings.DefaultSaveDestination = SaveDestination.Both;

            if (FormatComboBox.SelectedItem is string format)
                _settings.DefaultFileFormat = format;

            // Save hotkey settings
            if (HotKeyCaptureComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem captureItem)
                _settings.HotKeyCapture = captureItem.Content.ToString() ?? "PrintScreen";
            
            if (HotKeyRegionCaptureComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem regionItem)
                _settings.HotKeyRegionCapture = regionItem.Content.ToString() ?? "Ctrl+PrintScreen";

            _settingsService.SaveSettings(_settings);
            
            try
            {
                _settingsService.SetStartupWithWindows(_settings.RunOnStartup);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"Warning: {ex.Message}\nOther settings were saved successfully.", 
                    "Partial Success", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            var printScreenApplied = _settingsService.SetWindowsPrintScreenDisabled(_settings.DisableWindowsPrintScreen);

            // Reload hotkeys in the main window
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ReloadHotKeys();
            }

            var successMessage = printScreenApplied
                ? "Settings saved successfully! Hotkeys have been updated."
                : "Settings saved, but Windows Print Screen integration could not be fully updated. You may need to reopen the app as admin or change this setting in Windows Settings > Accessibility > Keyboard.";

            MessageBox.Show(successMessage, 
                printScreenApplied ? "Success" : "Partial Success",
                MessageBoxButton.OK,
                printScreenApplied ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to maximize/restore
            MaximizeRestore_Click(sender, e);
        }
        else if (e.ClickCount == 1)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if window state is changing or mouse is not pressed
                // Silently ignore these cases
            }
        }
    }
    
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    
    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (MaximizeRestoreButton != null)
            {
                MaximizeRestoreButton.Content = "🗖";
            }
        }
        else
        {
            WindowState = WindowState.Maximized;
            if (MaximizeRestoreButton != null)
            {
                MaximizeRestoreButton.Content = "🗗";
            }
        }
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
