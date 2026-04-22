# Contributing to Money Shot

First off, thank you for considering contributing to Money Shot! 💰

## Code of Conduct

Be respectful, be kind, be helpful. We're all here to make great software.

## How Can I Contribute?

### Reporting Bugs

Before creating a bug report, please check existing issues to avoid duplicates.

When creating a bug report, include:
- **Clear title and description**
- **Steps to reproduce** the issue
- **Expected behavior**
- **Actual behavior**
- **Screenshots** if applicable (preferably taken with Money Shot)
- **System information**:
  - Windows version
  - .NET version
  - Monitor configuration (if relevant)

### Suggesting Features

We love feature suggestions! Please include:
- **Clear use case** - why is this needed?
- **Proposed solution** - how should it work?
- **Alternatives considered**
- **Impact** - who benefits from this?

### Pull Requests

1. **Fork the repository**
2. **Create a feature branch** (`git checkout -b feature/AmazingFeature`)
3. **Make your changes**
4. **Test thoroughly**
5. **Commit your changes** (`git commit -m 'Add some AmazingFeature'`)
6. **Push to your fork** (`git push origin feature/AmazingFeature`)
7. **Open a Pull Request**

## Development Setup

See [DEVELOPER.md](DEVELOPER.md) for detailed setup instructions.

Quick start:
```bash
git clone https://github.com/YOUR_USERNAME/Money-Shot.git
cd Money-Shot
dotnet restore
dotnet build
dotnet run --project MoneyShot/MoneyShot.csproj
```

## Coding Guidelines

### C# Style
- Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable names
- Keep methods focused and single-purpose
- Add XML comments to public APIs
- Use nullable reference types

### Example
```csharp
/// <summary>
/// Captures a screenshot of the specified region.
/// </summary>
/// <param name="region">The region to capture</param>
/// <returns>A BitmapSource containing the screenshot</returns>
public BitmapSource CaptureRegion(Rectangle region)
{
    // Implementation
}
```

### Project Structure
```
MoneyShot/
├── Models/           # Data models and enums
├── Services/         # Business logic
├── Views/            # UI windows
├── Controls/         # Custom controls
└── Helpers/          # Utility classes
```

### Adding New Features

#### 1. New Annotation Tool
```csharp
// Add to Models/AnnotationTool.cs
public enum AnnotationTool
{
    // ...
    YourTool
}

// Add button to Views/EditorWindow.xaml
<Button Content="🎨 Your Tool" Tag="YourTool" Click="ToolButton_Click"/>

// Implement in Views/EditorWindow.xaml.cs
private Shape CreateYourTool()
{
    return new YourShape
    {
        Stroke = new SolidColorBrush(_currentColor),
        StrokeThickness = _lineThickness
    };
}
```

#### 2. New Service
```csharp
// Create Services/YourService.cs
namespace MoneyShot.Services;

public class YourService
{
    public void DoSomething()
    {
        // Implementation
    }
}

// Register in MainWindow.xaml.cs
private readonly YourService _yourService;

public MainWindow()
{
    // ...
    _yourService = new YourService();
}
```

#### 3. New Setting
```csharp
// Add to Models/AppSettings.cs
public bool YourSetting { get; set; } = false;

// Add UI in Views/SettingsWindow.xaml
<CheckBox Name="YourSettingCheckbox" Content="Your Setting"/>

// Load/Save in Views/SettingsWindow.xaml.cs
private void LoadSettings()
{
    YourSettingCheckbox.IsChecked = _settings.YourSetting;
}

private void Save_Click(object sender, RoutedEventArgs e)
{
    _settings.YourSetting = YourSettingCheckbox.IsChecked ?? false;
}
```

## Testing

### Manual Testing
- Test on Windows 10 and Windows 11
- Test with multiple monitors
- Test different DPI settings
- Test all annotation tools
- Test save to clipboard and file
- Test hotkeys
- Test system tray functionality
- Test startup integration

### Before Submitting PR
- [ ] Code builds without errors
- [ ] No compiler warnings
- [ ] All features work as expected
- [ ] Settings persist correctly
- [ ] No memory leaks (check with Task Manager)
- [ ] Tested on target Windows version

## Documentation

When adding features:
- Update README.md if user-facing
- Update FEATURES.md with details
- Update DEVELOPER.md if developer-facing
- Update CHANGELOG.md
- Add XML comments to public APIs

## Commit Messages

Use clear, descriptive commit messages:

```
Add blur tool for privacy protection

- Implement blur effect using Gaussian blur
- Add blur intensity slider
- Add button to toolbar
- Update documentation
```

Good commit messages:
- `Add region selection with interactive overlay`
- `Fix screenshot capture on high DPI displays`
- `Improve performance of annotation rendering`
- `Update README with installation instructions`

Avoid:
- `fix bug`
- `update stuff`
- `asdfasdf`

## Release Process

1. Update version number in project files
2. Update CHANGELOG.md with release notes
3. Create and test release build
4. Create GitHub release with tag
5. Upload release artifacts
6. Announce release

## Priority Areas

We'd especially appreciate contributions in these areas:

### High Priority
- [ ] Blur/pixelate tool implementation
- [ ] Enhanced text tool with font selection
- [ ] MSI installer using WiX
- [ ] Auto-update functionality
- [ ] Custom hotkey assignment

### Medium Priority
- [ ] Screenshot history viewer
- [ ] Freehand drawing tool
- [ ] Image effects (shadow, border)
- [ ] Multi-selection and grouping
- [ ] Template system

### Nice to Have
- [ ] Plugin system
- [ ] Cloud storage integration
- [ ] OCR text recognition
- [ ] Video/GIF recording
- [ ] Collaboration features

## Getting Help

- **Questions?** Open a GitHub Discussion
- **Stuck?** Check existing issues or open a new one
- **Need clarification?** Ask in your PR or issue

## Recognition

Contributors will be:
- Listed in project contributors
- Mentioned in release notes
- Forever appreciated! 🎉

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

---

Thank you for contributing to Money Shot! Every contribution, no matter how small, makes this project better. 💰

Happy coding! 🚀
