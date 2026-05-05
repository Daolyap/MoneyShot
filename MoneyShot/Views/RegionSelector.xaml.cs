using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DrawingRectangle = System.Drawing.Rectangle;

namespace MoneyShot.Views;

public partial class RegionSelector : Window
{
    private Point _startPoint;
    private Rectangle? _selectionRectangle;
    private bool _isSelecting;
    private int _virtualScreenLeft;
    private int _virtualScreenTop;
    private readonly BitmapSource _frozenScreen;

    public DrawingRectangle? SelectedRegion { get; private set; }
    public BitmapSource? CroppedScreenshot { get; private set; }

    public RegionSelector(BitmapSource frozenScreen)
    {
        InitializeComponent();
        _frozenScreen = frozenScreen;
        SetupFullScreenOverlay(frozenScreen);
    }

    private void SetupFullScreenOverlay(BitmapSource frozenScreen)
    {
        // Calculate virtual screen bounds (all monitors)
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            minX = Math.Min(minX, screen.Bounds.Left);
            minY = Math.Min(minY, screen.Bounds.Top);
            maxX = Math.Max(maxX, screen.Bounds.Right);
            maxY = Math.Max(maxY, screen.Bounds.Bottom);
        }

        _virtualScreenLeft = minX;
        _virtualScreenTop = minY;

        // Set window to cover all screens
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; // Required for transparent overlay with WindowStyle.None
        Topmost = true;
        
        // Position and size to cover entire virtual screen
        Left = minX;
        Top = minY;
        Width = maxX - minX;
        Height = maxY - minY;
        
        Cursor = Cursors.Cross;
        
        // Show immediately to prevent black screen
        ShowInTaskbar = false;
        
        // Display the frozen screen in the background
        try
        {
            // Set the frozen screenshot as the background
            if (BackgroundImage != null)
            {
                BackgroundImage.Source = frozenScreen;
                BackgroundImage.Stretch = Stretch.Fill;
            }
            
            // Add semi-transparent overlay on top
            Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error("Error displaying frozen screen", ex);
            // Fallback to just the overlay without frozen background
            Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(this);

            _selectionRectangle = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 0, 0))
            };

            Canvas.SetLeft(_selectionRectangle, _startPoint.X);
            Canvas.SetTop(_selectionRectangle, _startPoint.Y);
            SelectionCanvas.Children.Add(_selectionRectangle);
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isSelecting && _selectionRectangle != null)
        {
            var currentPoint = e.GetPosition(this);

            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(_startPoint.X - currentPoint.X);
            var height = Math.Abs(_startPoint.Y - currentPoint.Y);

            Canvas.SetLeft(_selectionRectangle, x);
            Canvas.SetTop(_selectionRectangle, y);
            _selectionRectangle.Width = width;
            _selectionRectangle.Height = height;
        }
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting && _selectionRectangle != null)
        {
            _isSelecting = false;

            var x = (int)Canvas.GetLeft(_selectionRectangle);
            var y = (int)Canvas.GetTop(_selectionRectangle);
            var width = (int)_selectionRectangle.Width;
            var height = (int)_selectionRectangle.Height;

            if (width > 10 && height > 10)
            {
                // Adjust coordinates to account for virtual screen offset
                // The canvas is positioned relative to the window, which starts at virtual screen origin
                var absoluteX = x + _virtualScreenLeft;
                var absoluteY = y + _virtualScreenTop;
                
                SelectedRegion = new DrawingRectangle(absoluteX, absoluteY, width, height);
                
                // Crop the selected region from the frozen screenshot
                try
                {
                    // The frozen screenshot coordinates are relative to the virtual screen
                    // Convert absolute screen coordinates to frozen screenshot coordinates
                    var cropX = absoluteX - _virtualScreenLeft;
                    var cropY = absoluteY - _virtualScreenTop;
                    
                    // Ensure coordinates are within bounds
                    cropX = Math.Max(0, Math.Min(cropX, _frozenScreen.PixelWidth - width));
                    cropY = Math.Max(0, Math.Min(cropY, _frozenScreen.PixelHeight - height));
                    width = Math.Min(width, _frozenScreen.PixelWidth - cropX);
                    height = Math.Min(height, _frozenScreen.PixelHeight - cropY);
                    
                    // Crop the image from the frozen screenshot
                    var croppedBitmap = new CroppedBitmap(_frozenScreen, new Int32Rect(cropX, cropY, width, height));
                    
                    // Freeze it to make it thread-safe
                    croppedBitmap.Freeze();
                    CroppedScreenshot = croppedBitmap;
                    
                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    MoneyShot.Services.Logger.Error("Error cropping frozen screenshot", ex);
                    DialogResult = false;
                }
            }
            else
            {
                DialogResult = false;
            }
            Close();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
