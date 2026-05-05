using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MoneyShot.Services;

public class SaveService
{
    public void SaveToClipboard(BitmapSource image)
    {
        try
        {
            Clipboard.SetImage(image);
        }
        catch (Exception ex)
        {
            Logger.Error("Error saving to clipboard", ex);
            throw new InvalidOperationException("Failed to save image to clipboard.", ex);
        }
    }

    public void SaveToFile(BitmapSource image, string filePath, string format = "PNG")
    {
        // Validate the file path to prevent path traversal
        ValidateFilePath(filePath);
        
        try
        {
            BitmapEncoder? encoder = format.ToUpper() switch
            {
                "PNG" => new PngBitmapEncoder(),
                "JPG" or "JPEG" => new JpegBitmapEncoder(),
                "BMP" => new BmpBitmapEncoder(),
                "GIF" => new GifBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };

            encoder.Frames.Add(BitmapFrame.Create(image));

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(fileStream);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Error("Access denied when saving file", ex);
            throw new InvalidOperationException("Access denied. Check file permissions.", ex);
        }
        catch (Exception ex)
        {
            Logger.Error("Error saving file", ex);
            throw new InvalidOperationException($"Failed to save image to file: {ex.Message}", ex);
        }
    }

    public string GenerateFileName(string format = "PNG")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"Screenshot_{timestamp}.{format.ToLower()}";
    }

    public void SaveImage(BitmapSource image, Models.SaveDestination destination, string? filePath = null, string format = "PNG")
    {
        switch (destination)
        {
            case Models.SaveDestination.Clipboard:
                SaveToClipboard(image);
                break;
            case Models.SaveDestination.File:
                if (filePath != null)
                    SaveToFile(image, filePath, format);
                break;
            case Models.SaveDestination.Both:
                SaveToClipboard(image);
                if (filePath != null)
                    SaveToFile(image, filePath, format);
                break;
        }
    }
    
    /// <summary>
    /// Validates file path to prevent path traversal and other security issues
    /// </summary>
    private void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));
        }
        
        try
        {
            // Get the full path and ensure it's valid
            var fullPath = Path.GetFullPath(filePath);
            
            // Ensure the path is rooted (absolute)
            if (!Path.IsPathRooted(fullPath))
            {
                throw new ArgumentException("Path must be absolute.", nameof(filePath));
            }
            
            // Get the directory path for additional validation
            var directory = Path.GetDirectoryName(fullPath);
            
            // Ensure we're not trying to write to a system directory
            var systemDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            
            foreach (var sysDir in systemDirs)
            {
                if (!string.IsNullOrEmpty(sysDir) && 
                    !string.IsNullOrEmpty(directory) && 
                    directory.StartsWith(sysDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Cannot save to system directories.", nameof(filePath));
                }
            }
            
            // Ensure the path is not a root directory
            if (Path.GetFileName(fullPath) == string.Empty)
            {
                throw new ArgumentException("Cannot save to a directory. Please specify a file name.", nameof(filePath));
            }
        }
        catch (ArgumentException)
        {
            // Re-throw ArgumentException as-is (our validation errors)
            throw;
        }
        catch (NotSupportedException ex)
        {
            throw new ArgumentException("Path format not supported.", nameof(filePath), ex);
        }
        catch (PathTooLongException ex)
        {
            throw new ArgumentException("Path is too long.", nameof(filePath), ex);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid file path.", nameof(filePath), ex);
        }
    }
}
