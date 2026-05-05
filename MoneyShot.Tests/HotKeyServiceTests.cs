using MoneyShot.Services;
using Xunit;

namespace MoneyShot.Tests;

public class HotKeyServiceTests
{
    [Theory]
    [InlineData("Ctrl+PrintScreen", HotKeyService.MOD_CONTROL, HotKeyService.VK_SNAPSHOT)]
    [InlineData("Alt+F1", HotKeyService.MOD_ALT, HotKeyService.VK_F1)]
    [InlineData("Shift+F12", HotKeyService.MOD_SHIFT, HotKeyService.VK_F12)]
    [InlineData("Win+1", HotKeyService.MOD_WIN, HotKeyService.VK_1)]
    [InlineData("Ctrl+Shift+1", HotKeyService.MOD_CONTROL | HotKeyService.MOD_SHIFT, HotKeyService.VK_1)]
    [InlineData("Ctrl+Alt+Shift+Win+0", HotKeyService.MOD_CONTROL | HotKeyService.MOD_ALT | HotKeyService.MOD_SHIFT | HotKeyService.MOD_WIN, HotKeyService.VK_0)]
    [InlineData("Control+F5", HotKeyService.MOD_CONTROL, HotKeyService.VK_F5)]
    [InlineData("Windows+9", HotKeyService.MOD_WIN, HotKeyService.VK_9)]
    [InlineData("PrtSc", 0u, HotKeyService.VK_SNAPSHOT)]
    [InlineData("PrintScreen", 0u, HotKeyService.VK_SNAPSHOT)]
    public void ParseHotKey_RecognizedCombinations(string input, uint expectedModifiers, uint expectedKey)
    {
        var (modifiers, key) = HotKeyService.ParseHotKey(input);
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("F1", HotKeyService.VK_F1)]
    [InlineData("F2", HotKeyService.VK_F2)]
    [InlineData("F3", HotKeyService.VK_F3)]
    [InlineData("F4", HotKeyService.VK_F4)]
    [InlineData("F5", HotKeyService.VK_F5)]
    [InlineData("F6", HotKeyService.VK_F6)]
    [InlineData("F7", HotKeyService.VK_F7)]
    [InlineData("F8", HotKeyService.VK_F8)]
    [InlineData("F9", HotKeyService.VK_F9)]
    [InlineData("F10", HotKeyService.VK_F10)]
    [InlineData("F11", HotKeyService.VK_F11)]
    [InlineData("F12", HotKeyService.VK_F12)]
    public void ParseHotKey_AllFunctionKeys(string input, uint expectedKey)
    {
        var (modifiers, key) = HotKeyService.ParseHotKey(input);
        Assert.Equal(0u, modifiers);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("0", HotKeyService.VK_0)]
    [InlineData("1", HotKeyService.VK_1)]
    [InlineData("9", HotKeyService.VK_9)]
    public void ParseHotKey_DigitKeys(string input, uint expectedKey)
    {
        var (_, key) = HotKeyService.ParseHotKey(input);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseHotKey_EmptyOrWhitespace_ReturnsZero(string? input)
    {
        var (modifiers, key) = HotKeyService.ParseHotKey(input!);
        Assert.Equal(0u, modifiers);
        Assert.Equal(0u, key);
    }

    [Theory]
    [InlineData("Garbage")]
    [InlineData("Ctrl+Garbage")]
    [InlineData("F13")]
    [InlineData("A")]
    [InlineData("Space")]
    public void ParseHotKey_UnknownKey_ReturnsZeroKey(string input)
    {
        var (_, key) = HotKeyService.ParseHotKey(input);
        Assert.Equal(0u, key);
    }

    [Theory]
    [InlineData("ctrl+printscreen")]
    [InlineData("CTRL+PRINTSCREEN")]
    [InlineData("CtRl+PrInTsCrEeN")]
    public void ParseHotKey_IsCaseInsensitive(string input)
    {
        var (modifiers, key) = HotKeyService.ParseHotKey(input);
        Assert.Equal(HotKeyService.MOD_CONTROL, modifiers);
        Assert.Equal(HotKeyService.VK_SNAPSHOT, key);
    }

    [Fact]
    public void ParseHotKey_ToleratesExtraSpaces()
    {
        var (modifiers, key) = HotKeyService.ParseHotKey("  Ctrl  +  PrintScreen  ");
        Assert.Equal(HotKeyService.MOD_CONTROL, modifiers);
        Assert.Equal(HotKeyService.VK_SNAPSHOT, key);
    }
}
