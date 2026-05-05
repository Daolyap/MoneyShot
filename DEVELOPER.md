# Developer Documentation

## Prerequisites

- .NET 8 SDK or later
- Windows 10/11 (for development and testing)
- Visual Studio 2022 or Visual Studio Code (recommended)

## Getting Started

### 1. Clone the Repository
```bash
git clone https://github.com/Daolyap/Money-Shot.git
cd Money-Shot
```

### 2. Restore Dependencies
```bash
dotnet restore MoneyShot/MoneyShot.csproj
```

### 3. Build the Project
```bash
# Debug build
dotnet build MoneyShot/MoneyShot.csproj --configuration Debug

# Release build
dotnet build MoneyShot/MoneyShot.csproj --configuration Release
```

### 4. Run Tests
```bash
dotnet test MoneyShot.Tests/MoneyShot.Tests.csproj
```

Tests cover:
- `HotKeyService.ParseHotKey` - Hotkey parsing with all modifier combinations
- `SettingsService.ValidateAndSanitizeSettings` - Settings validation and path sanitization
- `AutoUpdateService` - Version comparison, asset selection, SHA-256 verification

### 5. Run the Application
```bash
dotnet run --project MoneyShot/MoneyShot.csproj
```

## Project Structure

### Core Services

#### ScreenshotService
Handles screen capture functionality:
- `CaptureFullScreen()` - Captures all screens
- `CaptureRegion(Rectangle)` - Captures specific region
- `CaptureScreen(int)` - Captures single screen by index

#### SaveService
Manages saving screenshots:
- `SaveToClipboard(BitmapSource)` - Copies to clipboard
- `SaveToFile(BitmapSource, string, string)` - Saves to file
- `SaveImage(BitmapSource, SaveDestination, string, string)` - Combined save

#### SettingsService
Handles application settings:
- `LoadSettings()` - Loads user settings from JSON
- `SaveSettings(AppSettings)` - Saves settings to JSON
- `SetStartupWithWindows(bool)` - Configures Windows startup

#### HotKeyService
Manages global keyboard shortcuts:
- `Initialize(window)` - Initializes hotkey listener with window handle
- `RegisterHotKey(modifiers, key, action)` - Registers hotkey
- `UnregisterHotKey(id)` - Removes hotkey
- `ParseHotKey(string)` - Parses hotkey strings like "Ctrl+PrintScreen"
- Uses Win32 API for global hotkey registration

#### AutoUpdateService
Manages automatic updates:
- `GetAvailableUpdateAsync()` - Checks GitHub releases for new versions
- `StageAndPrepareUpdateAsync(updateInfo)` - Downloads and verifies update
- `ParseVersion(tag)` - Parses semantic version from release tags
- `CompareSemVer(v1, v2)` - Compares versions ignoring build number
- `SelectPreferredAsset(assets)` - Chooses .exe or .zip, skips MSI
- `ParseSha256Sums(content)` - Parses SHA-256 checksums file
- SHA-256 verification before installation for integrity checking

#### Logger
Static logging service:
- `Logger.Debug(message)` - Debug-level logging
- `Logger.Info(message)` - Info-level logging
- `Logger.Warn(message)` - Warning-level logging
- `Logger.Error(message, ex)` - Error-level logging with exception
- Writes to both `Debug.WriteLine` (development) and rolling file (release)
- Daily rolling logs at `%AppData%\MoneyShot\logs\` with 7-day retention

#### HistoryService
Manages screenshot history:
- `Save(image, width, height, source)` - Saves screenshot to history
- `List()` - Returns list of all saved captures
- `LoadImage(entry)` - Loads full-resolution image
- `LoadThumbnail(entry)` - Loads thumbnail (max 400px wide)
- `Delete(entry)` - Removes capture from history
- `EnforceRetention(maxCount)` - Enforces retention policy
- Stores captures in `%AppData%\MoneyShot\history` with JSON metadata

### UI Components

#### MainWindow
Main application interface:
- System tray integration
- Capture mode selection
- Settings access
- About information

#### EditorWindow
Screenshot annotation editor (~2000 lines, refactored):
- Toolbar with annotation tools (Rectangle, Circle, Arrow, Line, Text, Numbers, Blur)
- Custom color picker with 8 presets + full color browser
- Adjustable stroke thickness slider (1-10px)
- Canvas with zoom support
- Selection and resize handles (12×12 visual, 24×24 hit zone)
- Endpoint resize for arrows and lines
- Undo stack with all mutations
- Crop tool
- Save/copy functionality
- Keyboard shortcuts overlay (`?` key)
- Refactored with extracted components:
  - `Editor/UndoController.cs` - Undo stack and action records
  - `Editor/CanvasRenderer.cs` - Canvas capture and pixelation effects
  - `Editor/CanvasPosition.cs` - Canvas position helpers with NaN handling
  - `Editor/ElementResizeMode.cs` - Resize mode enumeration
  - `Editor/ElementState.cs` - Element state record for undo

#### RegionSelector
Full-screen overlay for region selection:
- Click and drag to select area
- Visual feedback with red rectangle
- ESC to cancel

#### SettingsWindow
Configuration interface:
- Save destination preferences
- File path and format selection
- Startup options
- Tray behavior
- Hotkey configuration
- History settings (capture toggle, retention count)

#### HistoryWindow
Screenshot history viewer:
- Thumbnail grid with metadata (timestamp, dimensions, source)
- Right-click context menu: Open in Editor, Copy to Clipboard, Delete
- "Open History Folder" button to browse saved captures
- Automatic thumbnail generation and caching

## Building for Release

### Create Portable Build
```bash
dotnet publish MoneyShot/MoneyShot.csproj \
  --configuration Release \
  --output ./publish \
  --runtime win-x64 \
  --self-contained false
```

### Create Self-Contained Build
```bash
dotnet publish MoneyShot/MoneyShot.csproj \
  --configuration Release \
  --output ./publish \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true
```

## GitHub Actions

The project includes automated CI/CD workflows:

### Workflows

1. **Build and Release** (`.github/workflows/release.yml`)
   - Triggers automatically on every push to main/master branch
   - Builds both portable ZIP and MSI installer
   - Creates a GitHub release automatically with:
     - Version tag: `v{version}-build.{build_number}.{commit_sha}`
     - Both MSI and ZIP artifacts attached
     - Generated release notes
   - Marked as pre-release for development builds
   - **Versioning**: Build number is synchronized across all artifacts:
     - Assembly version: `{version}.{build_number}` (e.g., `1.0.0.123`)
     - File version: `{version}.{build_number}`
     - MSI version: `{version}.{build_number}`

2. **Build** (`.github/workflows/build.yml`)
   - Runs on pull requests for validation
   - Builds and runs xUnit test suite
   - Creates build artifacts for review
   - Ensures code builds and tests pass before merge
   - Uses build number for version synchronization

3. **Build MSI Installer** (`.github/workflows/build-msi.yml`)
   - Runs on pull requests for validation
   - Tests MSI installer creation
   - Can be manually triggered via workflow_dispatch
   - Uses build number for MSI version

### Release Process

Releases are fully automated:
- Every merge to main/master triggers a new release
- Version is extracted from `MoneyShot/MoneyShot.csproj`
- Build number from `GITHUB_RUN_NUMBER` is appended to assembly and file versions
- MSI installer version matches the executable version
- SHA-256 checksums are generated for all release artifacts (zip and msi)
- SHA256SUMS.txt file is uploaded alongside binaries for integrity verification
- Build artifacts are automatically uploaded to GitHub Releases
- Users can download the latest build from the Releases page

### Version Synchronization

To ensure consistency, the build number is synchronized across:
- **Git Tag**: `v1.0.0-build.123.abc1234`
- **Assembly Version**: `1.0.0.123`
- **File Version**: `1.0.0.123`
- **MSI Version**: `1.0.0.123`

This is achieved by:
1. Extracting the base version from `MoneyShot.csproj`
2. Passing `/p:AssemblyVersion` and `/p:FileVersion` to `dotnet build`
3. Passing `-d ProductVersion` to the WiX toolset during MSI creation

## Adding New Features

### Adding a New Annotation Tool

1. Add enum value to `Models/AnnotationTool.cs`:
```csharp
public enum AnnotationTool
{
    // ... existing tools
    YourNewTool
}
```

2. Add button to `Views/EditorWindow.xaml`:
```xml
<Button Content="🎨 Your Tool" Tag="YourNewTool" Click="ToolButton_Click" ... />
```

3. Implement tool logic in `Views/EditorWindow.xaml.cs`:
```csharp
private Shape CreateYourTool()
{
    return new YourShape
    {
        Stroke = new SolidColorBrush(_currentColor),
        StrokeThickness = _lineThickness
    };
}
```

4. Add case to tool switch in `Canvas_MouseDown`:
```csharp
AnnotationTool.YourNewTool => CreateYourTool(),
```

5. If the tool supports resizing, add cases in `SelectElement` for your shape type and implement resize handlers if needed.

6. Add undo support by pushing action to `_undo.Push(...)` when tool is applied.

### Adding Screenshot History

The screenshot history feature is already implemented. To use it:

1. **Auto-save** - Captures are automatically saved to history if `AppSettings.SaveCapturesToHistory` is true
2. **Configure** - Users can toggle history and set retention count in Settings
3. **Access** - Users access history from tray menu → "History"

To save a new capture to history:
```csharp
_historyService.Save(bitmapImage, width, height, "Full Screen");
```

### Adding Logging

The `Logger` static facade is the centralized logging service. Use it throughout the codebase:

```csharp
// Debug-level (dev only)
Logger.Debug("Detailed diagnostic information");

// Info-level
Logger.Info("Screenshot captured successfully");

// Warning-level
Logger.Warn("Could not find setting, using default");

// Error-level
Logger.Error("Failed to save settings", exception);
```

Logs are written to:
- `Debug.WriteLine` for IDE debugging
- `%AppData%\MoneyShot\logs\moneyshot-YYYYMMDD.log` for production troubleshooting
- Automatic 7-day retention policy

### Adding Settings

1. Add property to `Models/AppSettings.cs`:
```csharp
public YourType YourSetting { get; set; } = defaultValue;
```

2. Add UI control to `Views/SettingsWindow.xaml`

3. Load/save in `Views/SettingsWindow.xaml.cs`:
```csharp
// In LoadSettings()
YourControl.Value = _settings.YourSetting;

// In Save_Click()
_settings.YourSetting = YourControl.Value;
```

## Debugging

### Enable Debug Output
Set breakpoints in Visual Studio or use debug logging:

```csharp
#if DEBUG
System.Diagnostics.Debug.WriteLine("Your debug message");
#endif
```

### Common Issues

**Hotkeys not working:**
- Check if another application is using the same hotkey
- Ensure the window has been initialized before registering hotkeys

**Screenshots appear black:**
- Some applications use protected rendering
- Try capturing the entire screen instead

**High DPI issues:**
- Application manifest includes DPI awareness settings
- Test on different DPI scaling levels

## Testing

### Automated Testing

The project includes a comprehensive xUnit test suite in `MoneyShot.Tests/`:

**Running Tests:**
```powershell
dotnet test MoneyShot.Tests/MoneyShot.Tests.csproj
```

**Test Coverage:**
- **HotKeyServiceTests** (19 tests)
  - Hotkey parsing with all modifier combinations
  - Case-insensitive key recognition
  - Whitespace handling
  - Unknown key rejection
  
- **SettingsServiceTests** (15 tests)
  - Path validation and sanitization
  - Relative/absolute path handling
  - Forbidden character detection
  - Line thickness clamping
  - File format validation
  
- **AutoUpdateServiceTests** (19 tests)
  - Semantic version comparison (ignoring build number)
  - Asset selection (.exe preference, .zip fallback)
  - SHA-256 hash verification
  - Checksum file parsing

### Manual Testing Checklist
- [ ] Full screen capture works on all monitors
- [ ] Region selection captures correct area
- [ ] All annotation tools draw and resize correctly
- [ ] Custom color picker opens and applies colors
- [ ] Stroke thickness slider updates line weight in real-time
- [ ] Endpoint resize works for arrows and lines
- [ ] Undo works for all mutations (at least 5 steps)
- [ ] Keyboard shortcuts overlay (`?`) displays correctly
- [ ] Save to clipboard works
- [ ] Save to file works
- [ ] Settings persist across restarts
- [ ] Hotkeys trigger captures (including custom hotkeys)
- [ ] System tray menu works
- [ ] Startup integration works
- [ ] Screenshot history appears in capture history window
- [ ] History right-click menu works (Open, Copy, Delete)
- [ ] Auto-update check works and verifies SHA-256

## Performance Considerations

- Screenshots are captured using GDI+ for maximum compatibility
- Large screenshots (multi-4K monitors) may take time to capture
- Annotation rendering uses WPF hardware acceleration when available

## Security Considerations

- No telemetry or data collection
- Settings stored locally in AppData
- No network requests
- Registry modifications only for startup integration (with user consent)

## Code Style

- Use C# 12 features where appropriate
- Follow Microsoft naming conventions
- Use nullable reference types
- Document public APIs with XML comments
- Keep methods focused and single-purpose

## Future Enhancements

See the roadmap in README.md for planned features.

## Design Notes

### Editor Architecture

The editor is the most complex part of the codebase (~2000 lines). Key invariants:

1. **Each resize handle owns its own MouseLeftButtonDown handler** - This prevents fragile manual hit-testing and snap-to-(0,0) bugs
2. **Don't clamp cursor-mode positions** - Clamping is correct for drawing new shapes but wrong for moving/resizing existing ones
3. **Initialize shapes with Width=0, Height=0** - Prevents shapes from briefly appearing at canvas (0,0)
4. **Use CanvasPosition helpers** - Centralized NaN handling for Canvas.GetLeft/Top
5. **Preserve state invariants** - _isResizing, _isEndpointResizing, _isDragging, _isDrawing are mutually exclusive

For more detailed guidance, see `CLAUDE.md` in the repo root.

## Future Enhancements

See `Opus-Speaks.md` for the roadmap of planned features, deferred work, and follow-up tasks.

## Getting Help

- Check existing issues on GitHub
- Review this documentation and `CLAUDE.md`
- Examine the code - it's well-structured with minimal comments
- Consult `Opus-Speaks.md` for context on deferred features and design decisions
- Open a new issue if you're stuck
