using System.IO;
using MoneyShot.Models;
using MoneyShot.Services;
using Xunit;

namespace MoneyShot.Tests;

public class SettingsServiceTests
{
    private static readonly string MyPictures =
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    [Fact]
    public void EmptyDefaultSavePath_FallsBackToMyPictures()
    {
        var settings = new AppSettings { DefaultSavePath = string.Empty };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal(MyPictures, sanitized.DefaultSavePath);
    }

    [Fact]
    public void NullDefaultSavePath_FallsBackToMyPictures()
    {
        var settings = new AppSettings { DefaultSavePath = null! };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal(MyPictures, sanitized.DefaultSavePath);
    }

    [Fact]
    public void RelativePath_ResolvesOrFallsBack()
    {
        var settings = new AppSettings { DefaultSavePath = "relative/path" };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.True(Path.IsPathRooted(sanitized.DefaultSavePath));
    }

    [Fact]
    public void AbsolutePath_IsPreservedAndNormalized()
    {
        var input = Path.Combine(Path.GetTempPath(), "screenshots");
        var settings = new AppSettings { DefaultSavePath = input };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal(Path.GetFullPath(input), sanitized.DefaultSavePath);
    }

    [Fact]
    public void PathWithDotDot_IsResolvedToFullPath()
    {
        var input = Path.Combine(Path.GetTempPath(), "a", "..", "b");
        var settings = new AppSettings { DefaultSavePath = input };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.True(Path.IsPathRooted(sanitized.DefaultSavePath));
        Assert.DoesNotContain("..", sanitized.DefaultSavePath);
    }

    [Theory]
    [InlineData("path<with>invalid|chars?")]
    [InlineData("CON:\\bad")]
    public void PathWithInvalidChars_FallsBackToMyPictures(string badPath)
    {
        var settings = new AppSettings { DefaultSavePath = badPath };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal(MyPictures, sanitized.DefaultSavePath);
    }

    [Theory]
    [InlineData("PNG", "PNG")]
    [InlineData("png", "png")]
    [InlineData("JPG", "JPG")]
    [InlineData("JPEG", "JPEG")]
    [InlineData("BMP", "BMP")]
    [InlineData("GIF", "GIF")]
    public void ValidFileFormat_IsPreserved(string input, string expected)
    {
        var settings = new AppSettings { DefaultFileFormat = input };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal(expected, sanitized.DefaultFileFormat);
    }

    [Theory]
    [InlineData("WEBP")]
    [InlineData("TIFF")]
    [InlineData("garbage")]
    [InlineData("")]
    public void UnknownFileFormat_FallsBackToPng(string input)
    {
        var settings = new AppSettings { DefaultFileFormat = input };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal("PNG", sanitized.DefaultFileFormat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(21)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void OutOfRangeLineThickness_ClampsToDefault(int input)
    {
        var settings = new AppSettings { DefaultLineThickness = input };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal(3, sanitized.DefaultLineThickness);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    public void InRangeLineThickness_IsPreserved(int input)
    {
        var settings = new AppSettings { DefaultLineThickness = input };
        var sanitized = SettingsService.ValidateAndSanitizeSettings(settings);
        Assert.Equal(input, sanitized.DefaultLineThickness);
    }
}
