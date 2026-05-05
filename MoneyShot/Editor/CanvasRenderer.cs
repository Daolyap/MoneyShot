using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MoneyShot.Editor;

/// <summary>
/// Pure rendering helpers extracted from EditorWindow. None of these depend on editor state
/// beyond their parameters, so they're easy to unit-test in isolation if we ever add WPF tests.
/// </summary>
internal static class CanvasRenderer
{
    private const int RenderDpi = 96;
    private const int PixelateBlockSize = 10;

    /// <summary>
    /// Renders the editor canvas to a bitmap matching the underlying image's pixel dimensions.
    /// Temporarily disables the zoom transform so the saved image is at native resolution.
    /// </summary>
    public static BitmapSource CaptureCanvasAsImage(FrameworkElement imageCanvas, BitmapSource originalImage, ScaleTransform zoomTransform)
    {
        var imageWidth = originalImage.PixelWidth;
        var imageHeight = originalImage.PixelHeight;

        var originalScaleX = zoomTransform.ScaleX;
        var originalScaleY = zoomTransform.ScaleY;
        zoomTransform.ScaleX = 1;
        zoomTransform.ScaleY = 1;

        imageCanvas.Measure(new Size(imageWidth, imageHeight));
        imageCanvas.Arrange(new Rect(0, 0, imageWidth, imageHeight));
        imageCanvas.UpdateLayout();

        var renderBitmap = new RenderTargetBitmap(imageWidth, imageHeight, RenderDpi, RenderDpi, PixelFormats.Pbgra32);
        renderBitmap.Render(imageCanvas);

        zoomTransform.ScaleX = originalScaleX;
        zoomTransform.ScaleY = originalScaleY;
        imageCanvas.UpdateLayout();

        return renderBitmap;
    }

    /// <summary>
    /// Builds an ImageBrush whose contents are a downsampled version of the area beneath the
    /// supplied rectangle, producing the classic "censor bar" pixelation effect.
    /// </summary>
    public static Brush CreatePixelatedBrush(Rectangle pixelateRect, BitmapSource originalImage)
    {
        var left = CanvasPosition.GetLeft(pixelateRect);
        var top = CanvasPosition.GetTop(pixelateRect);

        var width = (int)pixelateRect.Width;
        var height = (int)pixelateRect.Height;
        if (width <= 0 || height <= 0) return pixelateRect.Fill;

        try
        {
            var renderBitmap = new RenderTargetBitmap(originalImage.PixelWidth, originalImage.PixelHeight, RenderDpi, RenderDpi, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(originalImage, new Rect(0, 0, originalImage.PixelWidth, originalImage.PixelHeight));
            }
            renderBitmap.Render(visual);

            var pixelatedBitmap = new RenderTargetBitmap(width, height, RenderDpi, RenderDpi, PixelFormats.Pbgra32);
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                for (int y = 0; y < height; y += PixelateBlockSize)
                {
                    for (int x = 0; x < width; x += PixelateBlockSize)
                    {
                        var blockWidth = Math.Min(PixelateBlockSize, width - x);
                        var blockHeight = Math.Min(PixelateBlockSize, height - y);

                        var sampleX = (int)(left + x + blockWidth / 2);
                        var sampleY = (int)(top + y + blockHeight / 2);
                        sampleX = Math.Max(0, Math.Min(sampleX, originalImage.PixelWidth - 1));
                        sampleY = Math.Max(0, Math.Min(sampleY, originalImage.PixelHeight - 1));

                        var croppedBitmap = new CroppedBitmap(renderBitmap, new Int32Rect(sampleX, sampleY, 1, 1));
                        var pixels = new byte[4];
                        croppedBitmap.CopyPixels(pixels, 4, 0);

                        var color = Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
                        drawingContext.DrawRectangle(new SolidColorBrush(color), null, new Rect(x, y, blockWidth, blockHeight));
                    }
                }
            }
            pixelatedBitmap.Render(drawingVisual);

            return new ImageBrush(pixelatedBitmap) { Stretch = Stretch.Fill };
        }
        catch (ArgumentException)
        {
            return new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));
        }
        catch (InvalidOperationException)
        {
            return new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));
        }
    }
}
