using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MoneyShot.Services;

public class HotKeyService
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Dictionary<int, Action> _hotKeyActions = new();
    private int _currentId = 0;
    private IntPtr _windowHandle;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(HwndHook);
    }

    public int RegisterHotKey(uint modifiers, uint key, Action action)
    {
        _currentId++;
        if (RegisterHotKey(_windowHandle, _currentId, modifiers, key))
        {
            _hotKeyActions[_currentId] = action;
            return _currentId;
        }
        return -1;
    }

    public void UnregisterHotKey(int id)
    {
        UnregisterHotKey(_windowHandle, id);
        _hotKeyActions.Remove(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _hotKeyActions.Keys.ToList())
        {
            UnregisterHotKey(_windowHandle, id);
        }
        _hotKeyActions.Clear();
        _currentId = 0; // Reset ID counter
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_hotKeyActions.TryGetValue(id, out var action))
            {
                action?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    // Virtual key codes
    public const uint VK_SNAPSHOT = 0x2C; // Print Screen
    public const uint VK_0 = 0x30;
    public const uint VK_1 = 0x31;
    public const uint VK_2 = 0x32;
    public const uint VK_3 = 0x33;
    public const uint VK_4 = 0x34;
    public const uint VK_5 = 0x35;
    public const uint VK_6 = 0x36;
    public const uint VK_7 = 0x37;
    public const uint VK_8 = 0x38;
    public const uint VK_9 = 0x39;
    public const uint VK_F1 = 0x70;
    public const uint VK_F2 = 0x71;
    public const uint VK_F3 = 0x72;
    public const uint VK_F4 = 0x73;
    public const uint VK_F5 = 0x74;
    public const uint VK_F6 = 0x75;
    public const uint VK_F7 = 0x76;
    public const uint VK_F8 = 0x77;
    public const uint VK_F9 = 0x78;
    public const uint VK_F10 = 0x79;
    public const uint VK_F11 = 0x7A;
    public const uint VK_F12 = 0x7B;

    // Modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>
    /// Parse a hotkey string like "Ctrl+PrintScreen", "Alt+F1", or "Ctrl+Shift+1" into modifiers and key code
    /// </summary>
    public static (uint modifiers, uint key) ParseHotKey(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            return (0, 0);
        }

        uint modifiers = 0;
        uint key = 0;

        var parts = hotkeyString.Split('+');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToLower())
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= MOD_WIN;
                    break;
                case "printscreen":
                case "prtsc":
                    key = VK_SNAPSHOT;
                    break;
                case "0":
                    key = VK_0;
                    break;
                case "1":
                    key = VK_1;
                    break;
                case "2":
                    key = VK_2;
                    break;
                case "3":
                    key = VK_3;
                    break;
                case "4":
                    key = VK_4;
                    break;
                case "5":
                    key = VK_5;
                    break;
                case "6":
                    key = VK_6;
                    break;
                case "7":
                    key = VK_7;
                    break;
                case "8":
                    key = VK_8;
                    break;
                case "9":
                    key = VK_9;
                    break;
                case "f1":
                    key = VK_F1;
                    break;
                case "f2":
                    key = VK_F2;
                    break;
                case "f3":
                    key = VK_F3;
                    break;
                case "f4":
                    key = VK_F4;
                    break;
                case "f5":
                    key = VK_F5;
                    break;
                case "f6":
                    key = VK_F6;
                    break;
                case "f7":
                    key = VK_F7;
                    break;
                case "f8":
                    key = VK_F8;
                    break;
                case "f9":
                    key = VK_F9;
                    break;
                case "f10":
                    key = VK_F10;
                    break;
                case "f11":
                    key = VK_F11;
                    break;
                case "f12":
                    key = VK_F12;
                    break;
            }
        }

        return (modifiers, key);
    }

    /// <summary>
    /// Register a hotkey from a string like "Ctrl+PrintScreen"
    /// </summary>
    public int RegisterHotKeyFromString(string hotkeyString, Action action)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hotkeyString))
            {
                Logger.Warn("Hotkey string is null or empty");
                return -1;
            }
            
            var (modifiers, key) = ParseHotKey(hotkeyString);
            if (key == 0)
            {
                Logger.Warn($"Invalid hotkey: {hotkeyString}");
                return -1;
            }
            
            return RegisterHotKey(modifiers, key, action);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error registering hotkey '{hotkeyString}'", ex);
            return -1;
        }
    }
}
