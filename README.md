# Money Shot

A modern, feature-rich screenshot tool built for Windows with comprehensive annotation capabilities.

## Features

### Capture Modes
- **Full Screen Capture** - Capture all monitors at once
- **Region Selection** - Select and capture specific areas
- **Multi-Monitor Support** - Works seamlessly across multiple displays

### Annotation Tools
- **Shapes**: Rectangles, circles, arrows, and lines with endpoint resize
- **Freehand Drawing**: Draw free-form annotations with smooth curves
- **Colors**: 8 preset colors + custom color picker for unlimited options
- **Line Thickness**: Adjustable stroke width slider (1-10px)
- **Text**: Add text annotations with customizable fonts
- **Numbers**: Sequential numbering for step-by-step guides
- **Blur Tool**: Pixelate sensitive information
- **Undo**: Easily correct mistakes (undo multiple steps)
- **Keyboard Shortcuts**: Access help overlay with `?` key

### Save Options
- **Clipboard**: Copy screenshots directly to clipboard
- **File**: Save as PNG, JPG, or BMP
- **Both**: Save to both clipboard and file simultaneously
- **Custom Path**: Configure default save location

### Productivity Features
- **Global Hotkeys**:
  - `Print Screen` - Capture full screen (customizable)
  - `Ctrl + Print Screen` - Capture region (customizable)
  - `Ctrl + Shift + 1-9` - Capture individual monitors
- **Customizable Hotkeys**: Configure hotkeys in settings
- **Capture History**: Local screenshot history with thumbnails, search, and quick access
- **System Tray Integration**: Runs in background, accessible from tray
- **Startup Integration**: Optional auto-start with Windows
- **Auto-Update**: Automatic update checks with SHA-256 integrity verification
- **Modern UI**: Clean, dark-themed interface optimized for Windows 11
- **Keyboard Shortcuts**: Built-in shortcut guide accessible via `?` key

## Installation

### Option 1: MSI Installer (Recommended)
1. Go to [Releases](https://github.com/Daolyap/Money-Shot/releases)
2. Download the latest `MoneyShot-v*.msi` file
3. Run the MSI installer
4. The application will be installed to `Program Files\Money Shot`
5. Shortcuts will be created on the Desktop and Start Menu

> **Note**: Releases are automatically created with each update to the main branch. Look for the latest build with both MSI and ZIP downloads.

### Option 2: Portable ZIP
1. Go to [Releases](https://github.com/Daolyap/Money-Shot/releases)
2. Download the latest `MoneyShot-v*.zip` file
3. Extract the ZIP file
4. Run `MoneyShot.exe`

### Option 3: Build from Source
```bash
# Clone the repository
git clone https://github.com/Daolyap/Money-Shot.git
cd Money-Shot

# Build the project
dotnet build MoneyShot/MoneyShot.csproj --configuration Release

# Run the application
dotnet run --project MoneyShot/MoneyShot.csproj
```

## Usage

### Quick Start
1. Launch Money Shot
2. The application will minimize to the system tray
3. Use hotkeys to capture screenshots:
   - Press `Print Screen` for full screen capture
   - Press `Ctrl + Print Screen` for region selection
4. Annotate your screenshot in the editor
5. Save to clipboard or file

### Settings
Access settings from the main window or system tray menu to configure:
- Default save destination (clipboard, file, or both)
- Default save path and file format
- Run on Windows startup
- Minimize to tray behavior

## Technology Stack

- **Framework**: .NET 8
- **UI**: WPF (Windows Presentation Foundation)
- **Language**: C# 12
- **Target OS**: Windows 11+ (Windows 10 compatible)

## Architecture

```
MoneyShot/
├── Models/           # Data models and enums
├── Services/         # Business logic services
│   ├── ScreenshotService    # Screen capture functionality
│   ├── SaveService           # Save to clipboard/file
│   ├── SettingsService       # Settings management
│   └── HotKeyService         # Global hotkey registration
├── Views/            # UI windows
│   ├── MainWindow            # Main application window
│   ├── EditorWindow          # Screenshot editor with annotations
│   ├── RegionSelector        # Region selection overlay
│   └── SettingsWindow        # Settings configuration
└── Helpers/          # Utility classes
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## Roadmap

- [x] Full screen capture
- [x] Region selection
- [x] Basic annotation tools (rectangles, circles, arrows, lines)
- [x] Color selection (8 preset + custom picker)
- [x] Save to clipboard/file
- [x] Global hotkeys (customizable)
- [x] System tray integration
- [x] Windows startup integration
- [x] Modern UI with dark theme
- [x] GitHub Actions build workflow
- [x] MSI Installer package with auto-update
- [x] Advanced text tool with font selection
- [x] Blur/pixelate tool for privacy
- [x] Freehand drawing with smooth rendering
- [x] Individual monitor hotkeys (Ctrl+Shift+1-9)
- [x] Screenshot history with thumbnails
- [x] Auto-update with SHA-256 verification
- [x] Keyboard shortcuts help overlay
- [x] Adjustable stroke thickness
- [x] Endpoint resize for arrows and lines
- [ ] Image effects (drop shadow, borders)
- [ ] Quick share to cloud services
- [ ] Authenticode code signing

## License

This project is open source. See the LICENSE file for details.

## Known Issues

- Hotkeys may conflict with other applications
- High DPI scaling needs testing on various displays

## Why "Money Shot"?

Because every screenshot should be worth a thousand words... and dollars! 💰

## Support

If you encounter any issues or have suggestions, please [open an issue](https://github.com/Daolyap/Money-Shot/issues) on GitHub.

---

**Note**: This application is built specifically for Windows 11+ and requires .NET 8 Runtime to be installed.
