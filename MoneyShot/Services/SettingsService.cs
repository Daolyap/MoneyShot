using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using MoneyShot.Models;

namespace MoneyShot.Services;

public class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private readonly string _settingsPath;
    private readonly string _appDataPath;

    public SettingsService()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoneyShot"
        );
        Directory.CreateDirectory(_appDataPath);
        _settingsPath = Path.Combine(_appDataPath, SettingsFileName);
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            
            // Use secure deserialization options
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            var settings = JsonSerializer.Deserialize<AppSettings>(json, options);

            // Validate and sanitize loaded settings
            if (settings != null)
            {
                return ValidateAndSanitizeSettings(settings);
            }
            
            return new AppSettings();
        }
        catch (JsonException ex)
        {
            // Log the error (in a real app, use proper logging)
            Logger.Error("Error deserializing settings", ex);
            // Return default settings if deserialization fails
            return new AppSettings();
        }
        catch (Exception ex)
        {
            // Log unexpected errors
            Logger.Error("Unexpected error loading settings", ex);
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            // Validate and sanitize before saving
            settings = ValidateAndSanitizeSettings(settings);
            
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                // Don't serialize null values to reduce file size
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            };
            
            var json = JsonSerializer.Serialize(settings, options);
            
            // Write to temp file first, then rename (atomic operation)
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, true);
        }
        catch (Exception ex)
        {
            // Log the error
            Logger.Error("Error saving settings", ex);
            throw new InvalidOperationException("Failed to save settings. Please check file permissions.", ex);
        }
    }
    
    private static readonly char[] WindowsForbiddenPathChars = { '<', '>', '|', '?', '*', '"' };

    /// <summary>
    /// Validates and sanitizes settings to prevent path traversal and other security issues
    /// </summary>
    internal static AppSettings ValidateAndSanitizeSettings(AppSettings settings)
    {
        var fallbackSavePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        // Validate and sanitize save path to prevent path traversal
        if (!string.IsNullOrEmpty(settings.DefaultSavePath)
            && settings.DefaultSavePath.IndexOfAny(WindowsForbiddenPathChars) < 0
            && !ContainsControlChar(settings.DefaultSavePath))
        {
            try
            {
                // Get the full path and ensure it's a valid, absolute path
                var fullPath = Path.GetFullPath(settings.DefaultSavePath);

                // Ensure the path is rooted (absolute) and doesn't use relative components
                if (Path.IsPathRooted(fullPath) && Path.IsPathFullyQualified(settings.DefaultSavePath))
                {
                    settings.DefaultSavePath = fullPath;
                }
                else
                {
                    settings.DefaultSavePath = fallbackSavePath;
                }
            }
            catch (ArgumentException)
            {
                settings.DefaultSavePath = fallbackSavePath;
            }
            catch (NotSupportedException)
            {
                settings.DefaultSavePath = fallbackSavePath;
            }
            catch (PathTooLongException)
            {
                settings.DefaultSavePath = fallbackSavePath;
            }
        }
        else
        {
            settings.DefaultSavePath = fallbackSavePath;
        }
        
        // Validate file format
        var validFormats = new[] { "PNG", "JPG", "JPEG", "BMP", "GIF" };
        if (!validFormats.Contains(settings.DefaultFileFormat.ToUpper()))
        {
            settings.DefaultFileFormat = "PNG";
        }
        
        // Ensure line thickness is reasonable
        if (settings.DefaultLineThickness < 1 || settings.DefaultLineThickness > 20)
        {
            settings.DefaultLineThickness = 3;
        }
        
        return settings;
    }

    private static bool ContainsControlChar(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c)) return true;
        }
        return false;
    }

    public void SetStartupWithWindows(bool enabled)
    {
        const string appName = "MoneyShot";
        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            
            if (key == null)
            {
                throw new InvalidOperationException("Unable to access registry key for startup configuration.");
            }

            if (enabled)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                {
                    // Validate the path before writing to registry
                    var fullPath = Path.GetFullPath(exePath);
                    key.SetValue(appName, $"\"{fullPath}\"", RegistryValueKind.String);
                }
                else
                {
                    throw new InvalidOperationException("Unable to determine application path.");
                }
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error("Registry access denied", ex);
            throw new InvalidOperationException("Insufficient permissions to modify startup settings. Please run as administrator.", ex);
        }
        catch (Exception ex)
        {
            Logger.Error("Error setting startup configuration", ex);
            throw new InvalidOperationException("Failed to modify startup settings.", ex);
        }
    }

    public bool IsSetToRunOnStartup()
    {
        const string appName = "MoneyShot";
        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(appName) != null;
        }
        catch (Exception ex)
        {
            // Log but don't throw - this is a read-only operation
            Logger.Error("Error checking startup status", ex);
            return false;
        }
    }

    public bool SetWindowsPrintScreenDisabled(bool disabled)
    {
        try
        {
            // Open existing key first; fall back to creating it only when missing so this setting can still be applied.
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", true)
                ?? Registry.CurrentUser.CreateSubKey(@"Control Panel\Keyboard", true);
            if (key == null)
            {
                Logger.Warn("Unable to access keyboard settings in registry.");
                return false;
            }

            // Set PrintScreenKeyForSnippingEnabled to 0 to disable, 1 to enable
            // This controls Windows' "Use Print Screen to open screen capture" setting
            key.SetValue("PrintScreenKeyForSnippingEnabled", disabled ? 0 : 1, RegistryValueKind.DWord);
            key.Flush();

            return TryGetWindowsPrintScreenDisabled(out var isDisabled) && isDisabled == disabled;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error("Registry access denied", ex);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Error setting Print Screen configuration", ex);
            return false;
        }
    }

    public bool TryGetWindowsPrintScreenDisabled(out bool disabled)
    {
        disabled = false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", false);
            if (key == null)
            {
                return false;
            }

            var value = key.GetValue("PrintScreenKeyForSnippingEnabled");
            if (value is int intValue)
            {
                disabled = intValue == 0;
                return true;
            }

            if (value is string stringValue && int.TryParse(stringValue, out var parsedValue))
            {
                disabled = parsedValue == 0;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("Error reading Print Screen configuration", ex);
            return false;
        }
    }
}
