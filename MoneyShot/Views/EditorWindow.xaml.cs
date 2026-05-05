using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MoneyShot.Editor;
using MoneyShot.Models;
using MoneyShot.Services;

namespace MoneyShot.Views;

public partial class EditorWindow : Window
{
    private BitmapSource _originalImage;
    private AnnotationTool _currentTool = AnnotationTool.None;
    private Color _currentColor = Colors.Red;
    private Color _currentTextBackgroundColor = Colors.Transparent;
    private int _lineThickness = 3;
    private Point _startPoint;
    private Shape? _currentShape;
    private bool _isDrawing;
    private readonly SaveService _saveService;
    private readonly UndoController _undo = new();
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

    // Pixelate tool constant
    private const string PixelateTag = "pixelate";

    // Resize fields
    private bool _isResizing;
    private ElementResizeMode _resizeMode = ElementResizeMode.None;
    private Point _resizeStartPoint;
    private double _originalWidth;
    private double _originalHeight;
    private double _originalLeft;
    private double _originalTop;
    private double _originalTextFontSize;
    private ElementState? _resizeStartState;
    private readonly List<Rectangle> _resizeHandles = new();
    // Two-handle resize for Line / Path (arrow): stores the original endpoints and which one
    // the user grabbed. Kept separate from the box-resize fields to avoid intermingling state.
    private bool _isEndpointResizing;
    private bool _isResizingEndpointStart; // true = the "start" endpoint, false = the "end" endpoint
    private Point _originalEndpointStart;
    private Point _originalEndpointEnd;

    private const int FreehandMinDistance = 2; // Minimum pixel distance between points
    private const double ShapeUpdateMinDistancePixels = 1.5; // Minimum drag distance in pixels before updating shape geometry
    private const double MinResizeDimension = 10;
    private const double MinTextScaleFactor = 0.5;
    private const double MinTextFontSize = 8;
    // Visible square stays small but the click hit-zone is finger-sized to help on 4K displays.
    private const double HandleVisualSize = 12;
    private const double HandleHitZoneSize = 24;
    
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
        // While the shortcuts overlay is up, swallow the next key and dismiss it.
        if (ShortcutsOverlay != null && ShortcutsOverlay.Visibility == Visibility.Visible)
        {
            ShortcutsOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        // `?` (Shift+/) toggles the shortcut overlay regardless of other modifiers.
        if (e.Key == Key.OemQuestion && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ToggleShortcutsOverlay();
            e.Handled = true;
            return;
        }

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
            _undo.Push(new UndoController.RemoveElementUndoAction(_selectedElement, index));
            DrawingCanvas.Children.Remove(_selectedElement);
            ClearSelection();
        }
    }

    // Hooks invoked by UndoController action records. These remain on EditorWindow because
    // they touch private editor state (the canvas, selection, image), but the undo stack and
    // record types now live in MoneyShot.Editor.UndoController.
    internal void UndoAddElement(UIElement element)
    {
        DrawingCanvas.Children.Remove(element);
        if (_selectedElement == element)
        {
            ClearSelection();
        }
    }

    internal void UndoRemoveElement(UIElement element, int index)
    {
        if (DrawingCanvas.Children.Contains(element)) return;
        var targetIndex = Math.Max(0, Math.Min(index, DrawingCanvas.Children.Count));
        DrawingCanvas.Children.Insert(targetIndex, element);
    }

    internal void UndoCrop(BitmapSource previousImage, IReadOnlyList<UIElement> previousElements, int previousNumberCounter)
    {
        _originalImage = previousImage;
        DisplayImage();
        DrawingCanvas.Children.Clear();
        foreach (var element in previousElements)
        {
            DrawingCanvas.Children.Add(element);
        }
        _cropRectangle = null;
        _isCropping = false;
        _numberCounter = previousNumberCounter;
        _currentTool = AnnotationTool.Cursor;
        ClearSelection();
    }

    internal void UndoResize(UIElement element, ElementState previousState)
    {
        ApplyElementState(element, previousState);
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

    private Point ClampToCanvasBounds(Point point)
    {
        var clampedX = Math.Max(0, Math.Min(point.X, DrawingCanvas.Width));
        var clampedY = Math.Max(0, Math.Min(point.Y, DrawingCanvas.Height));
        return new Point(clampedX, clampedY);
    }

    private static bool AreElementStatesEqual(ElementState first, ElementState second)
    {
        const double epsilon = 0.01;
        var fontSizeEqual = (!first.FontSize.HasValue && !second.FontSize.HasValue) ||
                            (first.FontSize.HasValue && second.FontSize.HasValue &&
                             Math.Abs(first.FontSize.Value - second.FontSize.Value) < epsilon);

        return Math.Abs(first.Left - second.Left) < epsilon &&
               Math.Abs(first.Top - second.Top) < epsilon &&
               Math.Abs(first.Width - second.Width) < epsilon &&
               Math.Abs(first.Height - second.Height) < epsilon &&
               fontSizeEqual;
    }

    private static ElementState? CaptureElementState(UIElement element)
    {
        if (element is Shape shape && element is not Line && element is not Path)
        {
            return new ElementState(CanvasPosition.GetLeft(shape), CanvasPosition.GetTop(shape), shape.Width, shape.Height, null);
        }

        if (element is TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = textBlock.ActualWidth > 0 ? textBlock.ActualWidth : textBlock.DesiredSize.Width;
            var height = textBlock.ActualHeight > 0 ? textBlock.ActualHeight : textBlock.DesiredSize.Height;
            return new ElementState(CanvasPosition.GetLeft(textBlock), CanvasPosition.GetTop(textBlock), width, height, textBlock.FontSize);
        }

        return null;
    }

    private void ApplyElementState(UIElement element, ElementState state)
    {
        if (element is Shape shape && element is not Line)
        {
            shape.Width = state.Width;
            shape.Height = state.Height;
            Canvas.SetLeft(shape, state.Left);
            Canvas.SetTop(shape, state.Top);
        }
        else if (element is TextBlock textBlock)
        {
            if (state.FontSize.HasValue)
            {
                textBlock.FontSize = state.FontSize.Value;
            }
            Canvas.SetLeft(textBlock, state.Left);
            Canvas.SetTop(textBlock, state.Top);
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        else
        {
            return;
        }

        if (_selectedElement == element && _selectionBorder != null)
        {
            Canvas.SetLeft(_selectionBorder, state.Left - 2);
            Canvas.SetTop(_selectionBorder, state.Top - 2);
            _selectionBorder.Width = state.Width + 4;
            _selectionBorder.Height = state.Height + 4;
            UpdateResizeHandles(state.Left, state.Top, state.Width, state.Height);
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        // Cursor-mode operations (drag/resize) use the raw position so that
        // clamping does not corrupt _resizeStartPoint / _dragStartPoint and
        // cause snap jumps when the cursor re-enters the canvas.
        // Drawing tools use the clamped position to stay within the image bounds.
        var rawPoint = e.GetPosition(DrawingCanvas);
        var clickPoint = _currentTool == AnnotationTool.Cursor
            ? rawPoint
            : ClampToCanvasBounds(rawPoint);

        // Handle cursor mode for selection and moving.
        // Resize handles attach their own MouseLeftButtonDown handler and mark
        // the event Handled, so the canvas only sees clicks on the canvas
        // background or on annotation elements.
        if (_currentTool == AnnotationTool.Cursor)
        {
            var hitElement = FindElementAtPoint(rawPoint);

            if (hitElement != null)
            {
                SelectElement(hitElement);
                _isDragging = true;
                _dragStartPoint = rawPoint;
                DrawingCanvas.CaptureMouse();
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
            DrawingCanvas.CaptureMouse();
            return;
        }

        if (_currentTool == AnnotationTool.None)
            return;

        _isDrawing = true;
        _startPoint = clickPoint;
        _lastDrawPoint = clickPoint;
        DrawingCanvas.CaptureMouse();

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
                _undo.Push(new UndoController.AddElementUndoAction(element));
            }
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        // Raw position for cursor-mode (drag/resize) to avoid ClampToCanvasBounds
        // freezing _dragStartPoint at the canvas edge and causing jump-snaps when
        // the cursor re-enters from a different position.
        var rawPoint = e.GetPosition(DrawingCanvas);
        var currentPoint = ClampToCanvasBounds(rawPoint);

        // Handle resizing – use raw (unclamped) position
        if (_currentTool == AnnotationTool.Cursor && _isResizing && _selectedElement != null)
        {
            ResizeElement(_selectedElement, rawPoint);
            return;
        }

        // Endpoint resize for Line/Path (arrow)
        if (_currentTool == AnnotationTool.Cursor && _isEndpointResizing && _selectedElement != null)
        {
            ApplyEndpointResize(_selectedElement, rawPoint);
            return;
        }

        // Handle cursor mode for dragging elements – use raw (unclamped) position
        if (_currentTool == AnnotationTool.Cursor && _isDragging && _selectedElement != null)
        {
            var deltaX = rawPoint.X - _dragStartPoint.X;
            var deltaY = rawPoint.Y - _dragStartPoint.Y;
            
            MoveElement(_selectedElement, deltaX, deltaY);
            _dragStartPoint = rawPoint;
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
        if (DrawingCanvas.IsMouseCaptured)
        {
            DrawingCanvas.ReleaseMouseCapture();
        }

        if (_currentTool == AnnotationTool.Cursor)
        {
            if (_isResizing && _selectedElement != null && _resizeStartState != null)
            {
                var resizeEndState = CaptureElementState(_selectedElement);
                if (resizeEndState != null && !AreElementStatesEqual(_resizeStartState, resizeEndState))
                {
                    _undo.Push(new UndoController.ResizeUndoAction(_selectedElement, _resizeStartState));
                }
            }
            else if (_isEndpointResizing && _selectedElement != null)
            {
                // Endpoint resize finished — refresh selection so the bounding border + handles
                // sit on top of the new geometry.
                var element = _selectedElement;
                ClearSelection();
                SelectElement(element);
            }

            _isDragging = false;
            _isResizing = false;
            _isEndpointResizing = false;
            _resizeMode = ElementResizeMode.None;
            _resizeStartState = null;
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
                    pixelateRect.Fill = CanvasRenderer.CreatePixelatedBrush(pixelateRect, _originalImage);
                }
            }
            _undo.Push(new UndoController.AddElementUndoAction(_currentShape));
        }

        // Handle freehand polyline
        if (_isDrawing && _currentPolyline != null)
        {
            _undo.Push(new UndoController.AddElementUndoAction(_currentPolyline));
            _currentPolyline = null;
        }
        
        _isDrawing = false;
        _currentShape = null;
    }

    private Rectangle CreateRectangle()
    {
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(rect, _startPoint.X);
        Canvas.SetTop(rect, _startPoint.Y);
        return rect;
    }

    private Ellipse CreateEllipse()
    {
        var ellipse = new Ellipse
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = _lineThickness,
            Fill = Brushes.Transparent,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(ellipse, _startPoint.X);
        Canvas.SetTop(ellipse, _startPoint.Y);
        return ellipse;
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
                Background = new SolidColorBrush(_currentTextBackgroundColor),
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
            Tag = PixelateTag, // Tag to identify this as a pixelate rectangle
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(rect, _startPoint.X);
        Canvas.SetTop(rect, _startPoint.Y);
        return rect;
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

        var left = CanvasPosition.GetLeft(element);
        var top = CanvasPosition.GetTop(element);
        double width = 0, height = 0;

        if (element is Path path && path.Data is { } pathData)
        {
            // For paths (arrows), get bounds from geometry
            var bounds = pathData.Bounds;
            left += bounds.Left;
            top += bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
        }
        else if (element is Shape shape && element is not Line && element is not Path)
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

        // Choose handle style by element type:
        //   - Shape (rect/ellipse) and TextBlock get the 8-handle bounding-box layout.
        //   - Line and Path (arrow) get a 2-handle endpoint layout so each end can be moved
        //     independently without distorting the geometry.
        if ((element is Shape s && element is not Line && element is not Path) || element is TextBlock)
        {
            CreateResizeHandles(left, top, width, height);
        }
        else if (element is Line lineEl)
        {
            CreateEndpointHandles(new Point(lineEl.X1, lineEl.Y1), new Point(lineEl.X2, lineEl.Y2));
        }
        else if (element is Path arrowEl)
        {
            var (start, end) = TryGetArrowEndpoints(arrowEl);
            if (start.HasValue && end.HasValue)
            {
                CreateEndpointHandles(start.Value, end.Value);
            }
        }
    }

    private void CreateResizeHandles(double left, double top, double width, double height)
    {
        ClearResizeHandlesOnly();

        var handleColor = new SolidColorBrush(Colors.Blue);

        // 8-handle bounding box. Corner handles drive proportional resize, edges drive single-axis.
        _resizeHandles.Add(CreateResizeHandle(left - 2,                  top - 2,                   handleColor, ElementResizeMode.TopLeft));
        _resizeHandles.Add(CreateResizeHandle(left + width + 2,          top - 2,                   handleColor, ElementResizeMode.TopRight));
        _resizeHandles.Add(CreateResizeHandle(left - 2,                  top + height + 2,          handleColor, ElementResizeMode.BottomLeft));
        _resizeHandles.Add(CreateResizeHandle(left + width + 2,          top + height + 2,          handleColor, ElementResizeMode.BottomRight));
        _resizeHandles.Add(CreateResizeHandle(left + width / 2,          top - 2,                   handleColor, ElementResizeMode.Top));
        _resizeHandles.Add(CreateResizeHandle(left + width / 2,          top + height + 2,          handleColor, ElementResizeMode.Bottom));
        _resizeHandles.Add(CreateResizeHandle(left - 2,                  top + height / 2,          handleColor, ElementResizeMode.Left));
        _resizeHandles.Add(CreateResizeHandle(left + width + 2,          top + height / 2,          handleColor, ElementResizeMode.Right));
    }

    private void CreateEndpointHandles(Point start, Point end)
    {
        ClearResizeHandlesOnly();
        var handleColor = new SolidColorBrush(Colors.Blue);
        var startHandle = CreateEndpointHandle(start, handleColor, isStart: true);
        var endHandle   = CreateEndpointHandle(end,   handleColor, isStart: false);
        _resizeHandles.Add(startHandle);
        _resizeHandles.Add(endHandle);
    }

    private void ClearResizeHandlesOnly()
    {
        foreach (var handle in _resizeHandles)
        {
            DrawingCanvas.Children.Remove(handle);
        }
        _resizeHandles.Clear();
    }

    /// <summary>
    /// Reads the start/end points back out of an arrow's PathGeometry. Mirror of the construction
    /// done by <see cref="UpdateArrow"/> — first segment endpoint = StartPoint of the figure,
    /// final destination = first LineSegment's Point.
    /// </summary>
    private static void ShiftPathGeometry(PathGeometry geometry, double deltaX, double deltaY)
    {
        foreach (var figure in geometry.Figures)
        {
            figure.StartPoint = new Point(figure.StartPoint.X + deltaX, figure.StartPoint.Y + deltaY);
            foreach (var seg in figure.Segments)
            {
                if (seg is LineSegment ls)
                {
                    ls.Point = new Point(ls.Point.X + deltaX, ls.Point.Y + deltaY);
                }
            }
        }
    }

    private static (Point? start, Point? end) TryGetArrowEndpoints(Path arrow)
    {
        if (arrow.Data is not PathGeometry geometry || geometry.Figures.Count == 0) return (null, null);
        var figure = geometry.Figures[0];
        if (figure.Segments.Count == 0 || figure.Segments[0] is not LineSegment line) return (null, null);
        return (figure.StartPoint, line.Point);
    }

    private void UpdateResizeHandles(double left, double top, double width, double height)
    {
        if (_resizeHandles.Count != 8)
        {
            CreateResizeHandles(left, top, width, height);
            return;
        }

        // Handles are positioned by the centre of their hit zone, matching CreateResizeHandle.
        PositionHandle(_resizeHandles[0], left - 2,                  top - 2);
        PositionHandle(_resizeHandles[1], left + width + 2,          top - 2);
        PositionHandle(_resizeHandles[2], left - 2,                  top + height + 2);
        PositionHandle(_resizeHandles[3], left + width + 2,          top + height + 2);
        PositionHandle(_resizeHandles[4], left + width / 2,          top - 2);
        PositionHandle(_resizeHandles[5], left + width / 2,          top + height + 2);
        PositionHandle(_resizeHandles[6], left - 2,                  top + height / 2);
        PositionHandle(_resizeHandles[7], left + width + 2,          top + height / 2);
    }

    private static void PositionHandle(Rectangle handle, double centerX, double centerY)
    {
        Canvas.SetLeft(handle, centerX - handle.Width / 2);
        Canvas.SetTop(handle, centerY - handle.Height / 2);
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

    /// <summary>
    /// Builds a resize handle whose hit zone is finger-sized (24×24) but whose visible square
    /// stays small (12×12) so it doesn't dominate the canvas. The handle is a transparent
    /// Rectangle of HandleHitZoneSize containing a smaller Rectangle of HandleVisualSize as its
    /// fill via inner content; we cheat by using a Rectangle with a partially transparent fill
    /// — see the comment below — because Rectangle is the only Shape that natively works here
    /// without a wrapper.
    /// </summary>
    private Rectangle CreateResizeHandle(double centerX, double centerY, SolidColorBrush color, ElementResizeMode resizeMode)
    {
        // Use the visible-size Rectangle as the hit target. The hit area is enlarged via a
        // hidden expanded Border behind the visible square. To keep the implementation simple
        // we just make the actual Rectangle the hit-zone size with a small inner painted area.
        // (Implemented as a single Rectangle with hit-zone dimensions but a clipped visual.)
        var handle = new Rectangle
        {
            Width = HandleHitZoneSize,
            Height = HandleHitZoneSize,
            Fill = Brushes.Transparent, // hit zone — invisible
            Cursor = GetResizeCursor(resizeMode),
            Tag = resizeMode
        };
        // Layer a smaller visible square on top via a SolidColorBrush DrawingBrush so the user
        // sees a 12×12 marker but can click anywhere in the 24×24 zone.
        var inset = (HandleHitZoneSize - HandleVisualSize) / 2;
        var visual = new DrawingGroup();
        using (var dc = visual.Open())
        {
            dc.DrawRectangle(color, new Pen(Brushes.White, 1),
                new Rect(inset, inset, HandleVisualSize, HandleVisualSize));
        }
        handle.OpacityMask = null;
        handle.Fill = new DrawingBrush { Drawing = visual, Stretch = Stretch.None };

        handle.MouseLeftButtonDown += ResizeHandle_MouseLeftButtonDown;
        PositionHandle(handle, centerX, centerY);
        DrawingCanvas.Children.Add(handle);
        return handle;
    }

    /// <summary>
    /// Endpoint handles for Line/Path. Tagged with isStart=true/false instead of an
    /// ElementResizeMode, since they drive a different code path (BeginEndpointResize).
    /// </summary>
    private Rectangle CreateEndpointHandle(Point center, SolidColorBrush color, bool isStart)
    {
        var handle = new Rectangle
        {
            Width = HandleHitZoneSize,
            Height = HandleHitZoneSize,
            Fill = Brushes.Transparent,
            Cursor = Cursors.Cross,
            Tag = isStart ? EndpointTagStart : EndpointTagEnd
        };
        var inset = (HandleHitZoneSize - HandleVisualSize) / 2;
        var visual = new DrawingGroup();
        using (var dc = visual.Open())
        {
            dc.DrawEllipse(color, new Pen(Brushes.White, 1),
                new Point(inset + HandleVisualSize / 2, inset + HandleVisualSize / 2),
                HandleVisualSize / 2, HandleVisualSize / 2);
        }
        handle.Fill = new DrawingBrush { Drawing = visual, Stretch = Stretch.None };
        handle.MouseLeftButtonDown += EndpointHandle_MouseLeftButtonDown;
        PositionHandle(handle, center.X, center.Y);
        DrawingCanvas.Children.Add(handle);
        return handle;
    }

    private const string EndpointTagStart = "endpoint:start";
    private const string EndpointTagEnd = "endpoint:end";

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle handle || handle.Tag is not ElementResizeMode mode)
            return;
        if (_selectedElement == null)
            return;

        BeginResize(_selectedElement, mode, e.GetPosition(DrawingCanvas));
        e.Handled = true;
    }

    private void EndpointHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle handle || handle.Tag is not string tag)
            return;
        if (_selectedElement == null)
            return;

        var isStart = tag == EndpointTagStart;
        BeginEndpointResize(_selectedElement, isStart);
        e.Handled = true;
    }

    private void BeginEndpointResize(UIElement element, bool isStart)
    {
        if (element is Line line)
        {
            _originalEndpointStart = new Point(line.X1, line.Y1);
            _originalEndpointEnd = new Point(line.X2, line.Y2);
        }
        else if (element is Path arrow)
        {
            var (start, end) = TryGetArrowEndpoints(arrow);
            if (!start.HasValue || !end.HasValue) return;
            _originalEndpointStart = start.Value;
            _originalEndpointEnd = end.Value;
        }
        else
        {
            return;
        }

        _isEndpointResizing = true;
        _isResizingEndpointStart = isStart;
        DrawingCanvas.CaptureMouse();
    }

    private void BeginResize(UIElement element, ElementResizeMode mode, Point startPoint)
    {
        _isResizing = true;
        _resizeMode = mode;
        _resizeStartPoint = startPoint;
        _resizeStartState = CaptureElementState(element);

        if (element is Shape shape && element is not Line && element is not Path)
        {
            var width = shape.Width;
            var height = shape.Height;
            if (double.IsNaN(width) || width <= 0) width = shape.ActualWidth;
            if (double.IsNaN(height) || height <= 0) height = shape.ActualHeight;
            if (double.IsNaN(width) || width <= 0) width = MinResizeDimension;
            if (double.IsNaN(height) || height <= 0) height = MinResizeDimension;

            _originalWidth = width;
            _originalHeight = height;
            _originalLeft = Canvas.GetLeft(shape);
            _originalTop = Canvas.GetTop(shape);
            if (double.IsNaN(_originalLeft)) _originalLeft = 0;
            if (double.IsNaN(_originalTop)) _originalTop = 0;
        }
        else if (element is TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _originalWidth = textBlock.ActualWidth > 0 ? textBlock.ActualWidth : textBlock.DesiredSize.Width;
            _originalHeight = textBlock.ActualHeight > 0 ? textBlock.ActualHeight : textBlock.DesiredSize.Height;
            if (_originalWidth <= 0) _originalWidth = MinResizeDimension;
            if (_originalHeight <= 0) _originalHeight = MinResizeDimension;
            _originalLeft = Canvas.GetLeft(textBlock);
            _originalTop = Canvas.GetTop(textBlock);
            _originalTextFontSize = textBlock.FontSize;
            if (double.IsNaN(_originalLeft)) _originalLeft = 0;
            if (double.IsNaN(_originalTop)) _originalTop = 0;
        }
        else
        {
            _isResizing = false;
            _resizeStartState = null;
            return;
        }

        DrawingCanvas.CaptureMouse();
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
                newWidth = Math.Max(MinResizeDimension, _originalWidth + deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight + deltaY);
                break;
            case ElementResizeMode.BottomLeft:
                newWidth = Math.Max(MinResizeDimension, _originalWidth - deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight + deltaY);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                break;
            case ElementResizeMode.TopRight:
                newWidth = Math.Max(MinResizeDimension, _originalWidth + deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight - deltaY);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
            case ElementResizeMode.TopLeft:
                newWidth = Math.Max(MinResizeDimension, _originalWidth - deltaX);
                newHeight = Math.Max(MinResizeDimension, _originalHeight - deltaY);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
            case ElementResizeMode.Right:
                newWidth = Math.Max(MinResizeDimension, _originalWidth + deltaX);
                break;
            case ElementResizeMode.Left:
                newWidth = Math.Max(MinResizeDimension, _originalWidth - deltaX);
                newLeft = _originalLeft + (_originalWidth - newWidth);
                break;
            case ElementResizeMode.Bottom:
                newHeight = Math.Max(MinResizeDimension, _originalHeight + deltaY);
                break;
            case ElementResizeMode.Top:
                newHeight = Math.Max(MinResizeDimension, _originalHeight - deltaY);
                newTop = _originalTop + (_originalHeight - newHeight);
                break;
        }

        if (element is Shape shape && element is not Line && element is not Path)
        {
            shape.Width = newWidth;
            shape.Height = newHeight;
            Canvas.SetLeft(shape, newLeft);
            Canvas.SetTop(shape, newTop);
        }
        else if (element is TextBlock textBlock)
        {
            var scale = _resizeMode switch
            {
                ElementResizeMode.Left or ElementResizeMode.Right => _originalWidth > 0 ? newWidth / _originalWidth : 1,
                ElementResizeMode.Top or ElementResizeMode.Bottom => _originalHeight > 0 ? newHeight / _originalHeight : 1,
                _ => Math.Min(
                    _originalWidth > 0 ? newWidth / _originalWidth : 1,
                    _originalHeight > 0 ? newHeight / _originalHeight : 1)
            };
            scale = Math.Max(MinTextScaleFactor, scale);
            textBlock.FontSize = Math.Max(MinTextFontSize, _originalTextFontSize * scale);
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

        ClearResizeHandlesOnly();
        _selectedElement = null;
    }

    private void ApplyEndpointResize(UIElement element, Point newEndpoint)
    {
        // Drive the moving endpoint to the cursor; the other endpoint stays anchored at its
        // captured original position. Clamp to the canvas to avoid endpoints outside the image.
        var clamped = ClampToCanvasBounds(newEndpoint);
        var start = _isResizingEndpointStart ? clamped : _originalEndpointStart;
        var end   = _isResizingEndpointStart ? _originalEndpointEnd : clamped;

        if (element is Line line)
        {
            line.X1 = start.X; line.Y1 = start.Y;
            line.X2 = end.X;   line.Y2 = end.Y;
        }
        else if (element is Path arrow)
        {
            // Reuse UpdateArrow's geometry construction by temporarily swapping _startPoint /
            // _currentShape — that keeps a single source of truth for arrowhead math.
            var savedShape = _currentShape;
            var savedStart = _startPoint;
            _currentShape = arrow;
            _startPoint = start;
            UpdateArrow(end);
            _currentShape = savedShape;
            _startPoint = savedStart;
        }

        // Move the live handle marker to the cursor so the user has visual feedback.
        if (_resizeHandles.Count == 2)
        {
            var movingHandle = _isResizingEndpointStart ? _resizeHandles[0] : _resizeHandles[1];
            PositionHandle(movingHandle, clamped.X, clamped.Y);
        }
    }

    private void MoveElement(UIElement element, double deltaX, double deltaY)
    {
        if (element is Shape shape && element is not Line)
        {
            Canvas.SetLeft(shape, CanvasPosition.GetLeft(shape) + deltaX);
            Canvas.SetTop(shape, CanvasPosition.GetTop(shape) + deltaY);
        }
        else if (element is TextBlock textBlock)
        {
            Canvas.SetLeft(textBlock, CanvasPosition.GetLeft(textBlock) + deltaX);
            Canvas.SetTop(textBlock, CanvasPosition.GetTop(textBlock) + deltaY);
        }
        else if (element is Line line)
        {
            line.X1 += deltaX;
            line.Y1 += deltaY;
            line.X2 += deltaX;
            line.Y2 += deltaY;
        }
        else if (element is Path path && path.Data is PathGeometry geometry)
        {
            // Shift the geometry's points directly so the geometry stays canvas-absolute
            // (lets TryGetArrowEndpoints + endpoint handles read positions without offset math).
            ShiftPathGeometry(geometry, deltaX, deltaY);
        }

        // Update selection border position
        if (_selectionBorder != null)
        {
            Canvas.SetLeft(_selectionBorder, CanvasPosition.GetLeft(_selectionBorder) + deltaX);
            Canvas.SetTop(_selectionBorder, CanvasPosition.GetTop(_selectionBorder) + deltaY);
        }

        if ((element is Shape && element is not Line && element is not Path) || element is TextBlock)
        {
            var elementLeft = CanvasPosition.GetLeft(element);
            var elementTop = CanvasPosition.GetTop(element);

            double elementWidth;
            double elementHeight;
            if (element is TextBlock textBlock2)
            {
                elementWidth = textBlock2.ActualWidth;
                elementHeight = textBlock2.ActualHeight;
                if (elementWidth <= 0) elementWidth = textBlock2.RenderSize.Width;
                if (elementHeight <= 0) elementHeight = textBlock2.RenderSize.Height;
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
        else if (element is Line || element is Path)
        {
            // Endpoint handles need to follow Line/Path moves too. Re-running SelectElement
            // is the simplest correct path — it rebuilds the bounding border + endpoint handles.
            ClearResizeHandlesOnly();
            if (_selectionBorder != null) DrawingCanvas.Children.Remove(_selectionBorder);
            _selectionBorder = null;
            SelectElement(element);
        }
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string toolName)
        {
            if (Enum.TryParse<AnnotationTool>(toolName, out var tool))
            {
                _currentTool = tool;
                UpdateActiveToolButton(button);
            }
        }
    }

    /// <summary>
    /// Highlights the toolbar button for the active tool by swapping its style. Walks up the
    /// visual tree to find the WrapPanel that holds all tool buttons so we don't need named
    /// references for each one.
    /// </summary>
    private void UpdateActiveToolButton(Button activeButton)
    {
        var glassStyle = (Style)Resources["GlassButton"];
        var activeStyle = (Style)Resources["ActiveToolButton"];
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(activeButton);
        if (parent is not Panel toolPanel) return;
        foreach (var child in toolPanel.Children)
        {
            if (child is Button b && b.Tag is string)
            {
                b.Style = ReferenceEquals(b, activeButton) ? activeStyle : glassStyle;
            }
        }
    }

    private void CustomColorButton_Click(object sender, RoutedEventArgs e)
    {
        // Re-use the WinForms ColorDialog (already in our framework refs) rather than hand-rolling
        // an HSL picker — the OS picker is what most users expect and supports the full palette.
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(_currentColor.A, _currentColor.R, _currentColor.G, _currentColor.B)
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var picked = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
        _currentColor = picked;
        if (CustomColorButton != null)
        {
            CustomColorButton.Background = new SolidColorBrush(picked);
        }
        if (_selectedElement != null)
        {
            ChangeElementColor(_selectedElement, _currentColor);
        }
    }

    private void StrokeThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _lineThickness = (int)Math.Round(e.NewValue);
        if (StrokeThicknessLabel != null) StrokeThicknessLabel.Text = _lineThickness.ToString();
        // Live-update the selected element's stroke if it's a shape that supports it.
        if (_selectedElement is Shape shape && _selectedElement is not Path)
        {
            shape.StrokeThickness = _lineThickness;
        }
    }

    private void ShortcutsHelp_Click(object sender, RoutedEventArgs e) => ToggleShortcutsOverlay();

    private void ShortcutsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ShortcutsOverlay != null) ShortcutsOverlay.Visibility = Visibility.Collapsed;
    }

    private void ToggleShortcutsOverlay()
    {
        if (ShortcutsOverlay == null) return;
        ShortcutsOverlay.Visibility = ShortcutsOverlay.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
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

    private void TextBackgroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TextBackgroundComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        var colorName = selectedItem.Content?.ToString();
        _currentTextBackgroundColor = colorName switch
        {
            "White" => Colors.White,
            "Black" => Colors.Black,
            "Yellow" => Colors.Yellow,
            "Red" => Colors.Red,
            "Blue" => Colors.Blue,
            "Green" => Colors.Green,
            _ => Colors.Transparent
        };

        if (_selectedElement is TextBlock selectedText)
        {
            selectedText.Background = new SolidColorBrush(_currentTextBackgroundColor);
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
        _undo.Undo(this);
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
            var previousImage = _originalImage;
            var previousElements = new List<UIElement>();
            foreach (UIElement element in DrawingCanvas.Children)
            {
                if (element != _cropRectangle && element != _selectionBorder && !_resizeHandles.Contains(element))
                {
                    previousElements.Add(element);
                }
            }
            var previousNumberCounter = _numberCounter;

            // Create cropped bitmap
            var croppedBitmap = new CroppedBitmap(_originalImage,
                new Int32Rect(intCropX, intCropY, intCropWidth, intCropHeight));

            // Update the image
            _originalImage = croppedBitmap;
            DisplayImage();

            // Clear all annotations including crop rectangle
            DrawingCanvas.Children.Clear();
            _selectedElement = null;
            _selectionBorder = null;
            _resizeHandles.Clear();
            _cropRectangle = null;
            _numberCounter = 1;

            // Reset to cursor tool
            _currentTool = AnnotationTool.Cursor;
            _undo.Push(new UndoController.CropUndoAction(previousImage, previousElements, previousNumberCounter));
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

    private BitmapSource CaptureCanvasAsImage() =>
        CanvasRenderer.CaptureCanvasAsImage(ImageCanvas, _originalImage, ZoomTransform);
    
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
