using System.Windows;
using System.Windows.Controls;

namespace MoneyShot.Editor;

/// <summary>
/// Centralizes the NaN guards that used to be repeated everywhere we read Canvas.GetLeft/GetTop.
/// Canvas attached properties default to NaN when never set, which propagates through arithmetic
/// and ends up displayed as 0 — the cause of past snap-to-(0,0) regressions.
/// </summary>
internal static class CanvasPosition
{
    public static double GetLeft(UIElement element)
    {
        var value = Canvas.GetLeft(element);
        return double.IsNaN(value) ? 0 : value;
    }

    public static double GetTop(UIElement element)
    {
        var value = Canvas.GetTop(element);
        return double.IsNaN(value) ? 0 : value;
    }
}
