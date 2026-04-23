using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MoneyShot.Models;
using MoneyShot.Services;

namespace MoneyShot.Views;

public partial class EditorWindow : Window
{
    private BitmapSource _originalImage;
    private AnnotationTool _currentTool = AnnotationTool.None;
    private Color _currentColor = Colors.Red;
    private int _lineThickness = 3;
    private Point _startPoint;
    private Shape? _currentShape;
    private bool _isDrawing;
    private readonly SaveService _saveService;
    private readonly Stack<IUndoAction> _undoStack = new();
    private int _numberCounter = 1;
    
    // Selection/move fields
    private UIElement? _selectedElement;
    private Point _dragStartPoint;
    private bool _isDragging;
    private Border? _selectionBorder;
    private double _zoomLevel = 1.0;
    private const double ZoomIncrement = 0.25;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    
    // Pixelate tool constants
    private const string PixelateTag = "pixelate";
    private const int PixelateBlockSize = 10;
    private const int RenderDpi = 96;
    
    // Resize fields
    private bool _isResizing;
    private ElementResizeMode _resizeMode = ElementResizeMode.None;
    private Point _resizeStartPoint;
    private double _originalWidth;
    private double _originalHeight;
    private double _originalLeft;
    private double _originalTop;
    private double _originalTextFontSize;
    private List<Rectangle> _resizeHandles = new();
    
    private enum ElementResizeMode
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Top,
        Bottom,
        Left,
        Right
    }

    private interface IUndoAction
    {
        void Undo(EditorWindow window);
    }

    private sealed class AddElementUndoAction(UIElement element) : IUndoAction
    {
        public void Undo(EditorWindow window)
        {
            window.DrawingCanvas.Children.Remove(element);
            if (window._selectedElement == element)
            {
                window.ClearSelection();
            }
        }
    }

    private sealed class RemoveElementUndoAction(UIElement element, int index) : IUndoAction
    {
        public void Undo(EditorWindow window)
        {
            if (window.DrawingCanvas.Children.Contains(element))
            {
                return;
            }

            var targetIndex = Math.Max(0, Math.Min(index, window.DrawingCanvas.Children.Count));
            window.DrawingCanvas.Children.Insert(targetIndex, element);
        }
    }

    private sealed class CropUndoAction(BitmapSource previousImage, IReadOnlyList<UIElement> previousElements, int previousNumberCounter) : IUndoAction
    {
        public void Undo(EditorWindow window)
        {
            window._originalImage = previousImage;
            window.DisplayImage();
            window.DrawingCanvas.Children.Clear();
            foreach (var element in previousElements)
            {
                window.DrawingCanvas.Children.Add(element);
            }
            window._cropRectangle = null;
            window._isCropping = false;
            window._numberCounter = previousNumberCounter;
            window._currentTool = AnnotationTool.Cursor;
            window.ClearSelection();
        }
    }
    
    private const int FreehandMinDistance = 2; // Minimum pixel distance between points
    private const double ShapeUpdateMinDistancePixels = 1.5; // Minimum drag distance in pixels before updating shape geometry
    
    // Crop fields
    private Rectangle? _cropRectangle;
    private bool _isCropping;
    
    // Freehand drawing fields
    private Polyline? _currentPolyline;
    private Point _lastDrawPoint;
    
    // Cached pen for hit testing to avoid repeated allocations
    private static readonly Pen HitTestPen = new(Brushes.Black, 10);

    public EditorWindow(BitmapSource image)
    {
        InitializeComponent();
        _originalImage = image;
        _saveService = new SaveService();
        DisplayImage();
        SetupToolbar();
        
        // Add keyboard event handler for Delete key
        KeyDown += EditorWindow_KeyDown;
        
        // Add mouse wheel event handler for Ctrl + scroll zoom
        // Use PreviewMouseWheel to catch before ScrollViewer
        PreviewMouseWheel += EditorWindow_MouseWheel;
    }
    
    private void EditorWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Handle tool selection shortcuts
        if (!e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control) && 
            !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            switch (e.Key)
            {
                case Key.R:
                    _currentTool = AnnotationTool.Rectangle;
                    e.Handled = true;
                    break;
                case Key.C when !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control):
                    _currentTool = AnnotationTool.Circle;
                    e.Handled = true;
                    break;
                case Key.A when !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control):
                    _currentTool = AnnotationTool.Arrow;
                    e.Handled = true;
                    break;
                case Key.L:
                    _currentTool = AnnotationTool.Line;
                    e.Handled = true;
                    break;
                case Key.F:
                    _currentTool = AnnotationTool.Freehand;
                    e.Handled = true;
                    break;
                case Key.T:
                    _currentTool = AnnotationTool.Text;
                    e.Handled = true;
                    break;
                case Key.P:
                    _currentTool = AnnotationTool.Blur;
                    e.Handled = true;
                    break;
                case Key.D1:
                case Key.NumPad1:
                    _currentTool = AnnotationTool.Number;
                    e.Handled = true;
                    break;
                case Key.Escape:
                    // Close the editor window and cancel the screenshot
                    Close();
                    e.Handled = true;
                    break;
                case Key.Delete:
                    if (_selectedElement != null)
                    {
                        DeleteSelectedElement();
                        e.Handled = true;
                    }
                    break;
            }
        }
        
        // Handle Ctrl shortcuts
        if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.Z:
                    Undo_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.C:
                    SaveToClipboard_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    Save_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                case Key.Add:
                    ZoomIn_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomOut_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    ZoomReset_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
    }
    
    private void EditorWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only zoom when Ctrl is pressed
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Delta > 0)
            {
                // Scroll up = Zoom in
                if (_zoomLevel < MaxZoom)
                {
                    _zoomLevel += ZoomIncrement;
                    ApplyZoom();
                }
            }
            else
            {
                // Scroll down = Zoom out
                if (_zoomLevel > MinZoom)
                {
                    _zoomLevel -= ZoomIncrement;
                    ApplyZoom();
                }
            }
            e.Handled = true;
        }
    }
    
    private void DeleteSelectedElement()
    {
        if (_selectedElement != null)
        {
            var index = DrawingCanvas.Children.IndexOf(_selectedElement);
            _undoStack.Push(new RemoveElementUndoAction(_selectedElement, index));
            DrawingCanvas.Children.Remove(_selectedElement);
            ClearSelection();
        }
    }

    private void DisplayImage()
    {
        ImageDisplay.Source = _originalImage;
        ImageDisplay.Width = _originalImage.PixelWidth;
        ImageDisplay.Height = _originalImage.PixelHeight;
        
        // Update canvas size to match image
        DrawingCanvas.Width = _originalImage.PixelWidth;
        DrawingCanvas.Height = _originalImage.PixelHeight;
    }

    private void SetupToolbar()
    {
        // Tool buttons will be set up in XAML
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var clickPoint = e.GetPosition(DrawingCanvas);

        // Handle cursor mode for selection and moving
        if (_currentTool == AnnotationTool.Cursor)
        {
            // Check if clicking on a resize handle first
            var resizeHandle = FindResizeHandleAtPoint(clickPoint);
            if (resizeHandle != ElementResizeMode.None && _selectedElement != null)
            {
                _isResizing = true;
                _resizeMode = resizeHandle;
                _resizeStartPoint = clickPoint;
                
                // Store original dimensions
                if (_selectedElement is Shape shape && !(_selectedElement is Line))
                {
                    _originalWidth = shape.Width;
                    _originalHeight = shape.Height;
                    _originalLeft = Canvas.GetLeft(shape);
                    _originalTop = Canvas.GetTop(shape);
                    if (double.IsNaN(_originalLeft)) _originalLeft = 0;
                    if (double.IsNaN(_originalTop)) _originalTop = 0;
                }
                else if (_selectedElement is TextBlock textBlock)
                {
                    textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    _originalWidth = textBlock.ActualWidth > 0 ? textBlock.ActualWidth : textBlock.DesiredSize.Width;
                    _originalHeight = textBlock.ActualHeight > 0 ? textBlock.ActualHeight : textBlock.DesiredSize.Height;
                    _originalLeft = Canvas.GetLeft(textBlock);
                    _originalTop = Canvas.GetTop(textBlock);
                    _originalTextFontSize = textBlock.FontSize;
                    if (double.IsNaN(_originalLeft)) _originalLeft = 0;
                    if (double.IsNaN(_originalTop)) _originalTop = 0;
                }
                return;
            }
            
            // Try to find an element at the click position
            var hitElement = FindElementAtPoint(clickPoint);
            
            if (hitElement != null)
            {
                SelectElement(hitElement);
                _isDragging = true;
                _dragStartPoint = clickPoint;
            }
            else
            {
                ClearSelection();
            }
            return;
        }

        // Handle crop mode
        if (_currentTool == AnnotationTool.Crop)
        {
            _isCropping = true;
            _startPoint = clickPoint;
            
            // Remove existing crop rectangle if any
            if (_cropRectangle != null)
            {
                DrawingCanvas.Children.Remove(_cropRectangle);
            }
            
            _cropRectangle = new Rectangle
            {
                Stroke = new SolidColorBrush(Colors.Yellow),
                StrokeThickness = 2,
                StrokeDashArray = new System.Windows.Media.DoubleCollection(new[] { 4.0, 2.0 }),
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0))
            };
            
            DrawingCanvas.Children.Add(_cropRectangle);
            return;
        }

        if (_currentTool == AnnotationTool.None)
            return;

        _isDrawing = true;
        _startPoint = clickPoint;
        _lastDrawPoint = clickPoint;

        UIElement? element = _currentTool switch
        {
            AnnotationTool.Rectangle => CreateRectangle(),
            AnnotationTool.Circle => CreateEllipse(),
            AnnotationTool.Arrow => CreateArrow(),
            AnnotationTool.Line => CreateLine(),
            AnnotationTool.Freehand => CreatePolyline(),
            AnnotationTool.Number => CreateNumberLabel(),
            AnnotationTool.Text => CreateTextLabel(),
            AnnotationTool.Blur => CreateBlurRectangle(),
            _ => null
        };

        if (element != null)
        {
            DrawingCanvas.Children.Add(element);
            if (element is Shape shape)
            {
                _currentShape = shape;
            }
            else
            {
                _undoStack.Push(new AddElementUndoAction(element));
            }
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(DrawingCanvas);

        // Handle resizing
        if (_currentTool == AnnotationTool.Cursor && _isResizing && _selectedElement != null)
        {
            ResizeElement(_selectedElement, currentPoint);
            return;
        }

        // Handle cursor mode for dragging elements
        if (_currentTool == AnnotationTool.Cursor && _isDragging && _selectedElement != null)
        {
            var deltaX = currentPoint.X - _dragStartPoint.X;
            var deltaY = currentPoint.Y - _dragStartPoint.Y;
            
            MoveElement(_selectedElement, deltaX, deltaY);
            _dragStartPoint = currentPoint;
            return;
        }

        // Handle crop mode
        if (_currentTool == AnnotationTool.Crop && _isCropping && _cropRectangle != null)
        {
            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(_startPoint.X - currentPoint.X);
            var height = Math.Abs(_startPoint.Y - currentPoint.Y);

            Canvas.SetLeft(_cropRectangle, x);
            Canvas.SetTop(_cropRectangle, y);
            _cropRectangle.Width = width;
            _cropRectangle.Height = height;
            return;
        }

        if (!_isDrawing || _currentShape == null)
            return;

        if (_currentTool != AnnotationTool.Freehand)
        {
            var dx = currentPoint.X - _lastDrawPoint.X;
            var dy = currentPoint.Y - _lastDrawPoint.Y;
            if ((dx * dx) + (dy * dy) < ShapeUpdateMinDistancePixels * ShapeUpdateMinDistancePixels)
            {
                return;
            }
        }

        switch (_currentTool)
        {
            case AnnotationTool.Rectangle:
            case AnnotationTool.Blur:
                UpdateRectangle(currentPoint);
                break;
            case AnnotationTool.Circle:
                UpdateEllipse(currentPoint);
                break;
            case AnnotationTool.Line:
                UpdateLine(currentPoint);
                break;
            case AnnotationTool.Arrow:
                UpdateArrow(currentPoint);
                break;
            case AnnotationTool.Freehand:
                UpdatePolyline(currentPoint);
                break;
        }

        _lastDrawPoint = currentPoint;
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == AnnotationTool.Cursor)
        {
            _isDragging = false;
            _isResizing = false;
            _resizeMode = ElementResizeMode.None;
            return;
        }

        if (_currentTool == AnnotationTool.Crop && _isCropping)
        {
            _isCropping = false;
            if (_cropRectangle != null && _cropRectangle.Width > 10 && _cropRectangle.Height > 10)
            {
                // Ask user to confirm crop
                var result = MessageBox.Show(
                    "Apply crop to image? This will remove all annotations and crop the image.",
                    "Confirm Crop",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ApplyCrop();
                }
                else
                {
                    // Remove crop rectangle
                    DrawingCanvas.Children.Remove(_cropRectangle);
                    _cropRectangle = null;
                }
            }
            return;
        }

        if (_isDrawing && _currentShape != null)
        {
            // Apply pixelation effect if this is a pixelate rectangle
            if (_currentTool == AnnotationTool.Blur && _currentShape is Rectangle pixelateRect)
            {
                // Only apply pixelation if the rectangle has a reasonable size
                if (pixelateRect.Width > 5 && pixelateRect.Height > 5)
                {
                    pixelateRect.Fill = CreatePixelatedBrush(pixelateRect);
                }
            }
            _undoStack.Push(new AddElementUndoAction(_currentShape));
        }
        
        // Handle freehand polyline
        if (_isDrawing && _currentPolyline != null)
        {
            _undoStack.Push(new AddElementUndoAction(_currentPolyline));
            _currentPolyline = null;
        }
        
        _isDrawing = false;
        _currentShape = null;
    }

    private Rectangle CreateRectangle()
    {
        return new Rectangle
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent
        };
    }

    private Ellipse CreateEllipse()
    {
        return new Ellipse
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent
        };
    }

    private Line CreateLine()
    {
        return new Line
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            X1 = _startPoint.X,
            Y1 = _startPoint.Y,
            X2 = _startPoint.X,
            Y2 = _startPoint.Y
        };
    }

    private Path CreateArrow()
    {
        var path = new Path
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = new SolidColorBrush(_currentColor)
        };
        return path;
    }

    private Polyline CreatePolyline()
    {
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        polyline.Points.Add(_startPoint);
        _currentPolyline = polyline;
        return polyline;
    }

    private TextBlock CreateNumberLabel()
    {
        var textBlock = new TextBlock
        {
            Text = _numberCounter.ToString(),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_currentColor),
            Background = new SolidColorBrush(Colors.White),
            Padding = new Thickness(5)
        };
        Canvas.SetLeft(textBlock, _startPoint.X);
        Canvas.SetTop(textBlock, _startPoint.Y);
        _numberCounter++;
        _isDrawing = false; // Numbers don't need drag
        return textBlock;
    }

    private TextBlock? CreateTextLabel()
    {
        // Show a simple input dialog
        var inputDialog = new Window
        {
            Title = "Enter Text",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };

        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Enter text:",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 5)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new TextBox
        {
            Margin = new Thickness(0, 5, 0, 10),
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85))
        };
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var okButton = new Button
        {
            Content = "OK",
            Padding = new Thickness(20, 5, 20, 5),
            Background = new SolidColorBrush(Color.FromRgb(14, 99, 156)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(17, 119, 187)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        okButton.Click += (s, e) => inputDialog.DialogResult = true;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(20, 5, 20, 5),
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancelButton.Click += (s, e) => inputDialog.DialogResult = false;
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        inputDialog.Content = grid;
        textBox.Focus();

        if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            var textBlock = new TextBlock
            {
                Text = textBox.Text,
                FontSize = 16,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(_currentColor),
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Padding = new Thickness(5)
            };

            Canvas.SetLeft(textBlock, _startPoint.X);
            Canvas.SetTop(textBlock, _startPoint.Y);
            _isDrawing = false; // Text doesn't need drag
            return textBlock;
        }

        _isDrawing = false; // Text doesn't need drag
        return null;
    }

    private Rectangle CreateBlurRectangle()
    {
        // Create a rectangle that will pixelate the area underneath it
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(Colors.Transparent),
            StrokeThickness = 0,
            // Initially use a semi-transparent fill - will be replaced with pixelated image when drawn
            Fill = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128)),
            Tag = PixelateTag // Tag to identify this as a pixelate rectangle
        };
        return rect;
    }
    
    private Brush CreatePixelatedBrush(Rectangle pixelateRect)
    {
        // Get the position and size of the rectangle on the canvas
        var left = Canvas.GetLeft(pixelateRect);
        var top = Canvas.GetTop(pixelateRect);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        
        var width = (int)pixelateRect.Width;
        var height = (int)pixelateRect.Height;
        
        if (width <= 0 || height <= 0)
            return pixelateRect.Fill;
        
        try
        {
            // Create a render target to capture the image area
            var renderBitmap = new RenderTargetBitmap(
                (int)_originalImage.PixelWidth,
                (int)_originalImage.PixelHeight,
                RenderDpi, RenderDpi,
                PixelFormats.Pbgra32);
            
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(_originalImage, new Rect(0, 0, _originalImage.PixelWidth, _originalImage.PixelHeight));
            }
            renderBitmap.Render(visual);
            
            // Create a new bitmap for the pixelated version
            var pixelatedBitmap = new RenderTargetBitmap(
                width, height,
                RenderDpi, RenderDpi,
                PixelFormats.Pbgra32);
            
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                // Draw pixelated blocks
                for (int y = 0; y < height; y += PixelateBlockSize)
                {
                    for (int x = 0; x < width; x += PixelateBlockSize)
                    {
                        // Calculate the actual block size (handle edges)
                        var blockWidth = Math.Min(PixelateBlockSize, width - x);
                        var blockHeight = Math.Min(PixelateBlockSize, height - y);
                        
                        // Sample the center pixel of this block from the original image
                        var sampleX = (int)(left + x + blockWidth / 2);
                        var sampleY = (int)(top + y + blockHeight / 2);
                        
                        // Ensure we're within bounds
                        sampleX = Math.Max(0, Math.Min(sampleX, _originalImage.PixelWidth - 1));
                        sampleY = Math.Max(0, Math.Min(sampleY, _originalImage.PixelHeight - 1));
                        
                        // Get the color at this position
                        var croppedBitmap = new CroppedBitmap(renderBitmap, 
                            new Int32Rect(sampleX, sampleY, 1, 1));
                        
                        var pixels = new byte[4];
                        croppedBitmap.CopyPixels(pixels, 4, 0);
                        
                        var color = Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
                        
                        // Draw a rectangle with this color
                        drawingContext.DrawRectangle(
                            new SolidColorBrush(color),
                            null,
                            new Rect(x, y, blockWidth, blockHeight));
                    }
                }
            }
            
            pixelatedBitmap.Render(drawingVisual);
            
            // Create an ImageBrush from the pixelated bitmap
            return new ImageBrush(pixelatedBitmap)
            {
                Stretch = Stretch.Fill
            };
        }
        catch (ArgumentException)
        {
            // Fallback to a gray pattern if image dimensions are invalid
            return new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));
        }
        catch (InvalidOperationException)
        {
            // Fallback if rendering fails
            return new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));
        }
    }

    private void UpdateRectangle(Point currentPoint)
    {
        if (_currentShape is not Rectangle rect) return;

        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var width = Math.Abs(_startPoint.X - currentPoint.X);
        var height = Math.Abs(_startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = width;
        rect.Height = height;
    }

    private void UpdateEllipse(Point currentPoint)
    {
        if (_currentShape is not Ellipse ellipse) return;

        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var width = Math.Abs(_startPoint.X - currentPoint.X);
        var height = Math.Abs(_startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        ellipse.Width = width;
        ellipse.Height = height;
    }

    private void UpdateLine(Point currentPoint)
    {
        if (_currentShape is Line line)
        {
            line.X2 = currentPoint.X;
            line.Y2 = currentPoint.Y;
        }
    }

    private void UpdatePolyline(Point currentPoint)
    {
        if (_currentPolyline != null && _currentPolyline.Points.Count > 0)
        {
            // Add point if it's far enough from the last point to avoid too many points
            var lastPoint = _currentPolyline.Points[_currentPolyline.Points.Count - 1];
            var distance = Math.Sqrt(Math.Pow(currentPoint.X - lastPoint.X, 2) + Math.Pow(currentPoint.Y - lastPoint.Y, 2));
            
            if (distance > FreehandMinDistance) // Minimum distance threshold
            {
                _currentPolyline.Points.Add(currentPoint);
            }
        }
    }

    private void UpdateArrow(Point currentPoint)
    {
        if (_currentShape is not Path arrow) return;

        var dx = currentPoint.X - _startPoint.X;
        var dy = currentPoint.Y - _startPoint.Y;
        var angle = Math.Atan2(dy, dx);
        var length = Math.Sqrt(dx * dx + dy * dy);

        // Create arrow geometry
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = _startPoint };

        // Arrow line
        figure.Segments.Add(new LineSegment(currentPoint, true));

        // Arrow head
        var arrowHeadLength = Math.Min(20, length / 3);
        var arrowHeadAngle = Math.PI / 6; // 30 degrees

        var leftPoint = new Point(
            currentPoint.X - arrowHeadLength * Math.Cos(angle - arrowHeadAngle),
            currentPoint.Y - arrowHeadLength * Math.Sin(angle - arrowHeadAngle)
        );
        var rightPoint = new Point(
            currentPoint.X - arrowHeadLength * Math.Cos(angle + arrowHeadAngle),
            currentPoint.Y - arrowHeadLength * Math.Sin(angle + arrowHeadAngle)
        );

        figure.Segments.Add(new LineSegment(leftPoint, false));
        figure.Segments.Add(new LineSegment(currentPoint, true));
        figure.Segments.Add(new LineSegment(rightPoint, true));
        figure.Segments.Add(new LineSegment(currentPoint, true));

        geometry.Figures.Add(figure);
        arrow.Data = geometry;
    }

    private UIElement? FindElementAtPoint(Point point)
    {
        // Search through canvas children in reverse order (top to bottom)
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            var element = DrawingCanvas.Children[i];
            
            // Skip the selection border and resize handles
            if (element == _selectionBorder || _resizeHandles.Contains(element))
                continue;
            
            // Check if point is within element bounds
            if (IsPointInElement(element, point))
            {
                return element;
            }
        }
        return null;
    }

    private bool IsPointInElement(UIElement element, Point point)
    {
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        
        // Handle NaN values (elements without explicit positioning)
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        if (element is Path path)
        {
            // For paths (arrows), use geometry-based hit testing
            if (path.Data is PathGeometry pathGeometry)
            {
                // Adjust point for canvas positioning
                var adjustedPoint = new Point(point.X - left, point.Y - top);
                return pathGeometry.FillContains(adjustedPoint) || 
                       pathGeometry.StrokeContains(HitTestPen, adjustedPoint);
            }
            return false;
        }
        else if (element is Shape shape && !(element is Line))
        {
            var width = shape.Width;
            var height = shape.Height;
            
            if (double.IsNaN(width) || double.IsNaN(height))
                return false;
                
            return point.X >= left && point.X <= left + width &&
                   point.Y >= top && point.Y <= top + height;
        }
        else if (element is TextBlock textBlock)
        {
            var width = textBlock.ActualWidth;
            var height = textBlock.ActualHeight;
            
            return point.X >= left && point.X <= left + width &&
                   point.Y >= top && point.Y <= top + height;
        }
        else if (element is Line line)
        {
            // Check if point is near the line
            var distance = DistanceFromPointToLine(point, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
            return distance < 10; // 10 pixel tolerance
        }
        
        return false;
    }

    private double DistanceFromPointToLine(Point p, Point lineStart, Point lineEnd)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;
        var lengthSquared = dx * dx + dy * dy;
        
        if (lengthSquared == 0)
            return Math.Sqrt((p.X - lineStart.X) * (p.X - lineStart.X) + (p.Y - lineStart.Y) * (p.Y - lineStart.Y));
        
        var t = Math.Max(0, Math.Min(1, ((p.X - lineStart.X) * dx + (p.Y - lineStart.Y) * dy) / lengthSquared));
        var projX = lineStart.X + t * dx;
        var projY = lineStart.Y + t * dy;
        
        return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }

    private void SelectElement(UIElement element)
    {
        ClearSelection();
        _selectedElement = element;
        
        // Add visual indicator for selection
        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Blue),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };
        
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        
        double width = 0, height = 0;
        
        if (element is Path path)
        {
            // For paths, get bounds from geometry
            var bounds = path.Data.Bounds;
            left += bounds.Left;
            top += bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
        }
        else if (element is Shape shape && !(element is Line))
        {
            width = shape.Width;
            height = shape.Height;
        }
        else if (element is TextBlock textBlock)
        {
            width = textBlock.ActualWidth;
            height = textBlock.ActualHeight;
        }
        else if (element is Line line)
        {
            left = Math.Min(line.X1, line.X2);
            top = Math.Min(line.Y1, line.Y2);
            width = Math.Abs(line.X2 - line.X1);
            height = Math.Abs(line.Y2 - line.Y1);
        }
        
        Canvas.SetLeft(_selectionBorder, left - 2);
        Canvas.SetTop(_selectionBorder, top - 2);
        _selectionBorder.Width = width + 4;
        _selectionBorder.Height = height + 4;
        
        DrawingCanvas.Children.Add(_selectionBorder);
        
        // Add resize handles for resizable elements
        if ((element is Shape shape2 && !(element is Line)) || element is TextBlock)
        {
            CreateResizeHandles(left, top, width, height);
        }
    }
    
    private void CreateResizeHandles(double left, double top, double width, double height)
    {
        // Clear existing handles
        foreach (var handle in _resizeHandles)
        {
            DrawingCanvas.Children.Remove(handle);
        }
        _resizeHandles.Clear();
        
        var handleSize = 8.0;
        var handleColor = new SolidColorBrush(Colors.Blue);
        
        // Corner handles
        _resizeHandles.Add(CreateResizeHandle(left - 2 - handleSize/2, top - 2 - handleSize/2, handleSize, handleColor, ElementResizeMode.TopLeft)); // Top-left
        _resizeHandles.Add(CreateResizeHandle(left + width + 2 - handleSize/2, top - 2 - handleSize/2, handleSize, handleColor, ElementResizeMode.TopRight)); // Top-right
        _resizeHandles.Add(CreateResizeHandle(left - 2 - handleSize/2, top + height + 2 - handleSize/2, handleSize, handleColor, ElementResizeMode.BottomLeft)); // Bottom-left
        _resizeHandles.Add(CreateResizeHandle(left + width + 2 - handleSize/2, top + height + 2 - handleSize/2, handleSize, handleColor, ElementResizeMode.BottomRight)); // Bottom-right
        
        // Edge handles
        _resizeHandles.Add(CreateResizeHandle(left + width/2 - handleSize/2, top - 2 - handleSize/2, handleSize, handleColor, ElementResizeMode.Top)); // Top
        _resizeHandles.Add(CreateResizeHandle(left + width/2 - handleSize/2, top + height + 2 - handleSize/2, handleSize, handleColor, ElementResizeMode.Bottom)); // Bottom
        _resizeHandles.Add(CreateResizeHandle(left - 2 - handleSize/2, top + height/2 - handleSize/2, handleSize, handleColor, ElementResizeMode.Left)); // Left
        _resizeHandles.Add(CreateResizeHandle(left + width + 2 - handleSize/2, top + height/2 - handleSize/2, handleSize, handleColor, ElementResizeMode.Right)); // Right
    }

    private void UpdateResizeHandles(double left, double top, double width, double height)
    {
        if (_resizeHandles.Count != 8)
        {
            CreateResizeHandles(left, top, width, height);
            return;
        }

        var handleSize = _resizeHandles[0].Width;

        Canvas.SetLeft(_resizeHandles[0], left - 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[0], top - 2 - handleSize / 2);

        Canvas.SetLeft(_resizeHandles[1], left + width + 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[1], top - 2 - handleSize / 2);

        Canvas.SetLeft(_resizeHandles[2], left - 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[2], top + height + 2 - handleSize / 2);

        Canvas.SetLeft(_resizeHandles[3], left + width + 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[3], top + height + 2 - handleSize / 2);

        Canvas.SetLeft(_resizeHandles[4], left + width / 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[4], top - 2 - handleSize / 2);

        Canvas.SetLeft(_resizeHandles[5], left + width / 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[5], top + height + 2 - handleSize / 2);

        Canvas.SetLeft(_resizeHandles[6], left - 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[6], top + height / 2 - handleSize / 2);

        Canvas.SetLeft(_resizeHandles[7], left + width + 2 - handleSize / 2);
        Canvas.SetTop(_resizeHandles[7], top + height / 2 - handleSize / 2);
    }
    
    private static Cursor GetResizeCursor(ElementResizeMode resizeMode)
    {
        return resizeMode switch
        {
            ElementResizeMode.TopLeft => Cursors.SizeNWSE,
            ElementResizeMode.BottomRight => Cursors.SizeNWSE,
            ElementResizeMode.TopRight => Cursors.SizeNESW,
            ElementResizeMode.BottomLeft => Cursors.SizeNESW,
            ElementResizeMode.Top => Cursors.SizeNS,
            ElementResizeMode.Bottom => Cursors.SizeNS,
            ElementResizeMode.Left => Cursors.SizeWE,
            ElementResizeMode.Right => Cursors.SizeWE,
            _ => Cursors.SizeAll
        };
    }

    private Rectangle CreateResizeHandle(double x, double y, double size, SolidColorBrush color, ElementResizeMode resizeMode)
    {
        var handle = new Rectangle
        {
            Width = size,
            Height = size,
            Fill = color,
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 1,
            Cursor = GetResizeCursor(resizeMode)
        };
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
        DrawingCanvas.Children.Add(handle);
        return handle;
    }
    
    private ElementResizeMode FindResizeHandleAtPoint(Point point)
    {
        if (_resizeHandles.Count != 8) return ElementResizeMode.None;
        
        var tolerance = 5.0;
        
        for (int i = 0; i < _resizeHandles.Count; i++)
        {
            var handle = _resizeHandles[i];
            var left = Canvas.GetLeft(handle);
            var top = Canvas.GetTop(handle);
            
            if (point.X >= left - tolerance && point.X <= left + handle.Width + tolerance &&
                point.Y >= top - tolerance && point.Y <= top + handle.Height + tolerance)
            {
                return i switch
                {
                    0 => ElementResizeMode.TopLeft,
                    1 => ElementResizeMode.TopRight,
                    2 => ElementResizeMode.BottomLeft,
                    3 => ElementResizeMode.BottomRight,
                    4 => ElementResizeMode.Top,
                    5 => ElementResizeMode.Bottom,
                    6 => ElementResizeMode.Left,
                    7 => ElementResizeMode.Right,
                    _ => ElementResizeMode.None
                };
            }
        }
        
        return ElementResizeMode.None;
    }
    
    private void ResizeElement(UIElement element, Point currentPoint)
    {
        var deltaX = currentPoint.X - _resizeStartPoint.X;
        var deltaY = currentPoint.Y - _resizeStartPoint.Y;

        var newLeft = _originalLeft;
        var newTop = _originalTop;
        var newWidth = _originalWidth;
        var newHeight = _originalHeight;

        switch (_resizeMode)
        {
            case ElementResizeMode.BottomRight:
                newWidth = Math.Max(10, _originalWidth + deltaX);
                newHeight = Math.Max(10, _originalHeight + deltaY);
                break;
            case ElementResizeMode.BottomLeft:
                newWidth = Math.Max(10, _originalWidth - deltaX);
                newHeight = Math.Max(10, _originalHeight + deltaY);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                break;
            case ElementResizeMode.TopRight:
                newWidth = Math.Max(10, _originalWidth + deltaX);
                newHeight = Math.Max(10, _originalHeight - deltaY);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
            case ElementResizeMode.TopLeft:
                newWidth = Math.Max(10, _originalWidth - deltaX);
                newHeight = Math.Max(10, _originalHeight - deltaY);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
            case ElementResizeMode.Right:
                newWidth = Math.Max(10, _originalWidth + deltaX);
                break;
            case ElementResizeMode.Left:
                newWidth = Math.Max(10, _originalWidth - deltaX);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                break;
            case ElementResizeMode.Bottom:
                newHeight = Math.Max(10, _originalHeight + deltaY);
                break;
            case ElementResizeMode.Top:
                newHeight = Math.Max(10, _originalHeight - deltaY);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
        }

        if (element is Shape shape && element is not Line)
        {
            shape.Width = newWidth;
            shape.Height = newHeight;
            Canvas.SetLeft(shape, newLeft);
            Canvas.SetTop(shape, newTop);
        }
        else if (element is TextBlock textBlock)
        {
            var widthScale = _originalWidth > 0 ? newWidth / _originalWidth : 1;
            var heightScale = _originalHeight > 0 ? newHeight / _originalHeight : 1;
            var scale = _resizeMode switch
            {
                ElementResizeMode.Left or ElementResizeMode.Right => widthScale,
                ElementResizeMode.Top or ElementResizeMode.Bottom => heightScale,
                _ => Math.Min(widthScale, heightScale)
            };
            scale = Math.Max(0.5, scale);
            textBlock.FontSize = Math.Max(8, _originalTextFontSize * scale);
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(textBlock, newLeft);
            Canvas.SetTop(textBlock, newTop);
            newWidth = textBlock.DesiredSize.Width;
            newHeight = textBlock.DesiredSize.Height;
        }
        else
        {
            return;
        }

        if (_selectionBorder != null)
        {
            Canvas.SetLeft(_selectionBorder, newLeft - 2);
            Canvas.SetTop(_selectionBorder, newTop - 2);
            _selectionBorder.Width = newWidth + 4;
            _selectionBorder.Height = newHeight + 4;
        }

        UpdateResizeHandles(newLeft, newTop, newWidth, newHeight);
    }

    private void ClearSelection()
    {
        if (_selectionBorder != null)
        {
            DrawingCanvas.Children.Remove(_selectionBorder);
            _selectionBorder = null;
        }
        
        // Remove resize handles
        foreach (var handle in _resizeHandles)
        {
            DrawingCanvas.Children.Remove(handle);
        }
        _resizeHandles.Clear();
        
        _selectedElement = null;
    }

    private void MoveElement(UIElement element, double deltaX, double deltaY)
    {
        if (element is Shape shape && !(element is Line))
        {
            var left = Canvas.GetLeft(shape);
            var top = Canvas.GetTop(shape);
            
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            
            Canvas.SetLeft(shape, left + deltaX);
            Canvas.SetTop(shape, top + deltaY);
        }
        else if (element is TextBlock textBlock)
        {
            var left = Canvas.GetLeft(textBlock);
            var top = Canvas.GetTop(textBlock);
            
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            
            Canvas.SetLeft(textBlock, left + deltaX);
            Canvas.SetTop(textBlock, top + deltaY);
        }
        else if (element is Line line)
        {
            line.X1 += deltaX;
            line.Y1 += deltaY;
            line.X2 += deltaX;
            line.Y2 += deltaY;
        }
        else if (element is Path path)
        {
            // For paths (arrows), use Canvas positioning like other elements
            var left = Canvas.GetLeft(path);
            var top = Canvas.GetTop(path);
            
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            
            Canvas.SetLeft(path, left + deltaX);
            Canvas.SetTop(path, top + deltaY);
        }
        
        // Update selection border position
        if (_selectionBorder != null)
        {
            var borderLeft = Canvas.GetLeft(_selectionBorder);
            var borderTop = Canvas.GetTop(_selectionBorder);
            
            if (double.IsNaN(borderLeft)) borderLeft = 0;
            if (double.IsNaN(borderTop)) borderTop = 0;
            
            Canvas.SetLeft(_selectionBorder, borderLeft + deltaX);
            Canvas.SetTop(_selectionBorder, borderTop + deltaY);
        }

        if ((element is Shape && element is not Line) || element is TextBlock)
        {
            var elementLeft = Canvas.GetLeft(element);
            var elementTop = Canvas.GetTop(element);
            if (double.IsNaN(elementLeft)) elementLeft = 0;
            if (double.IsNaN(elementTop)) elementTop = 0;

            double elementWidth;
            double elementHeight;
            if (element is TextBlock textBlock)
            {
                elementWidth = textBlock.ActualWidth;
                elementHeight = textBlock.ActualHeight;
                if (elementWidth <= 0) elementWidth = textBlock.RenderSize.Width;
                if (elementHeight <= 0) elementHeight = textBlock.RenderSize.Height;
            }
            else if (element is Shape shapeElement)
            {
                elementWidth = shapeElement.Width;
                elementHeight = shapeElement.Height;
            }
            else
            {
                return;
            }

            if (elementWidth > 0 && elementHeight > 0)
            {
                UpdateResizeHandles(elementLeft, elementTop, elementWidth, elementHeight);
            }
        }
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string toolName)
        {
            if (Enum.TryParse<AnnotationTool>(toolName, out var tool))
            {
                _currentTool = tool;
            }
        }
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Background is SolidColorBrush brush)
        {
            _currentColor = brush.Color;
            
            // If an element is selected, change its color
            if (_selectedElement != null)
            {
                ChangeElementColor(_selectedElement, _currentColor);
            }
        }
    }
    
    private void ChangeElementColor(UIElement element, Color newColor)
    {
        if (!IsColorChangeableElement(element))
            return;
            
        var brush = new SolidColorBrush(newColor);
        
        if (element is Shape shape)
        {
            shape.Stroke = brush;
            if (element is Path path)
            {
                path.Fill = brush; // Arrows use fill for the arrowhead
            }
        }
        else if (element is TextBlock textBlock)
        {
            textBlock.Foreground = brush;
        }
    }
    
    private bool IsColorChangeableElement(UIElement element)
    {
        // Pixelate rectangles have DrawingBrush and should not be color-changed
        if (element is Rectangle rect && rect.Fill is DrawingBrush)
            return false;
            
        return element is Shape || element is TextBlock;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count > 0)
        {
            var action = _undoStack.Pop();
            action.Undo(this);
        }
    }

    private void ApplyCrop()
    {
        if (_cropRectangle == null) return;

        var cropX = Canvas.GetLeft(_cropRectangle);
        var cropY = Canvas.GetTop(_cropRectangle);
        var cropWidth = _cropRectangle.Width;
        var cropHeight = _cropRectangle.Height;

        // Validate and clamp crop dimensions
        cropX = Math.Max(0, cropX);
        cropY = Math.Max(0, cropY);
        cropX = Math.Min(cropX, _originalImage.PixelWidth - 1);
        cropY = Math.Min(cropY, _originalImage.PixelHeight - 1);
        
        cropWidth = Math.Min(cropWidth, _originalImage.PixelWidth - cropX);
        cropHeight = Math.Min(cropHeight, _originalImage.PixelHeight - cropY);
        
        // Ensure positive dimensions
        var intCropX = (int)Math.Round(cropX);
        var intCropY = (int)Math.Round(cropY);
        var intCropWidth = (int)Math.Round(cropWidth);
        var intCropHeight = (int)Math.Round(cropHeight);
        
        if (intCropWidth <= 0 || intCropHeight <= 0)
        {
            MessageBox.Show("Invalid crop dimensions.", "Crop Error", MessageBoxButton.OK, MessageBoxImage.Error);
            DrawingCanvas.Children.Remove(_cropRectangle);
            _cropRectangle = null;
            return;
        }

        try
        {
            ClearSelection();

            var previousImage = _originalImage;
            var previousElements = DrawingCanvas.Children
                .Cast<UIElement>()
                .Where(element => element != _cropRectangle && element != _selectionBorder && !_resizeHandles.Contains(element))
                .ToList();
            var previousNumberCounter = _numberCounter;

            // Create cropped bitmap
            var croppedBitmap = new CroppedBitmap(_originalImage,
                new Int32Rect(intCropX, intCropY, intCropWidth, intCropHeight));

            // Update the image
            _originalImage = croppedBitmap;
            DisplayImage();

            // Clear all annotations including crop rectangle
            DrawingCanvas.Children.Clear();
            _cropRectangle = null;
            _numberCounter = 1;

            // Reset to cursor tool
            _currentTool = AnnotationTool.Cursor;
            _undoStack.Push(new CropUndoAction(previousImage, previousElements, previousNumberCounter));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply crop: {ex.Message}", "Crop Error", MessageBoxButton.OK, MessageBoxImage.Error);
            DrawingCanvas.Children.Remove(_cropRectangle);
            _cropRectangle = null;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var finalImage = CaptureCanvasAsImage();
            
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
                DefaultExt = ".png",
                FileName = _saveService.GenerateFileName("PNG")
            };

            if (saveDialog.ShowDialog() == true)
            {
                var extension = System.IO.Path.GetExtension(saveDialog.FileName).TrimStart('.');
                var format = GetFileFormat(extension);
                _saveService.SaveToFile(finalImage, saveDialog.FileName, format);
                MessageBox.Show("Image saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GetFileFormat(string extension)
    {
        return extension.ToUpper() switch
        {
            "JPG" or "JPEG" => "JPG",
            "BMP" => "BMP",
            _ => "PNG"
        };
    }

    private void SaveToClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var finalImage = CaptureCanvasAsImage();
            _saveService.SaveToClipboard(finalImage);
            MessageBox.Show("Image copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy image to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_zoomLevel < MaxZoom)
        {
            _zoomLevel += ZoomIncrement;
            ApplyZoom();
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_zoomLevel > MinZoom)
        {
            _zoomLevel -= ZoomIncrement;
            ApplyZoom();
        }
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        _zoomLevel = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        ZoomTransform.ScaleX = _zoomLevel;
        ZoomTransform.ScaleY = _zoomLevel;
    }

    private BitmapSource CaptureCanvasAsImage()
    {
        // Get the actual image dimensions (not the canvas display size)
        var imageWidth = (int)_originalImage.PixelWidth;
        var imageHeight = (int)_originalImage.PixelHeight;
        
        // Temporarily remove zoom transform for rendering
        var originalScaleX = ZoomTransform.ScaleX;
        var originalScaleY = ZoomTransform.ScaleY;
        ZoomTransform.ScaleX = 1;
        ZoomTransform.ScaleY = 1;
        
        // Force layout update to ensure proper rendering
        ImageCanvas.Measure(new Size(imageWidth, imageHeight));
        ImageCanvas.Arrange(new Rect(0, 0, imageWidth, imageHeight));
        ImageCanvas.UpdateLayout();
        
        var renderBitmap = new RenderTargetBitmap(
            imageWidth,
            imageHeight,
            96, 96,
            PixelFormats.Pbgra32);

        renderBitmap.Render(ImageCanvas);
        
        // Restore zoom transform
        ZoomTransform.ScaleX = originalScaleX;
        ZoomTransform.ScaleY = originalScaleY;
        
        // Force layout update again to restore zoom
        ImageCanvas.UpdateLayout();
        
        return renderBitmap;
    }
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to maximize/restore
            MaximizeRestore_Click(sender, e);
        }
        else if (e.ClickCount == 1)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if window state is changing or mouse is not pressed
                // Silently ignore these cases
            }
        }
    }
    
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    
    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (MaximizeRestoreButton != null)
            {
                MaximizeRestoreButton.Content = "🗖";
            }
        }
        else
        {
            WindowState = WindowState.Maximized;
            if (MaximizeRestoreButton != null)
            {
                MaximizeRestoreButton.Content = "🗗";
            }
        }
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
