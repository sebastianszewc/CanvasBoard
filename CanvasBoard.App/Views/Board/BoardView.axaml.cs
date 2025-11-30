using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CanvasBoard.App.Views.Board;

public partial class BoardView : UserControl
{
    private Grid _boardHost = null!;
    private Grid _viewport = null!;
    private MatrixTransform _viewTransform = null!;
    private Canvas _imageLayer = null!;

    // world -> screen:  screen = world * _zoom + _offset
    private double _zoom = 1.0;
    private Vector _offset = new Vector(0, 0);

    private bool _isPanning;
    private Point _lastPointerPos;

    private const double MinZoom = 0.1;
    private const double MaxZoom = 4.0;
    private const double ZoomFactorStep = 0.1; // 10% per wheel tick

    // Selection + resize state
    private Image? _selectedImage;
    private Border? _selectionOutline;
    private readonly Border[] _resizeHandles = new Border[4];
    private bool _isResizing;
    private int _activeHandleIndex = -1;
    private Rect _originalImageRect;

    // Drag-move state
    private bool _isDraggingImage;
    private Point _dragStartWorld;
    private Point _dragStartImageTopLeft;

    public BoardView()
    {
        InitializeComponent();

        _boardHost = this.FindControl<Grid>("BoardHost")
                     ?? throw new InvalidOperationException("BoardHost not found.");
        _viewport = this.FindControl<Grid>("Viewport")
                   ?? throw new InvalidOperationException("Viewport not found.");

        _viewTransform = _viewport.RenderTransform as MatrixTransform
                         ?? throw new InvalidOperationException("Viewport.RenderTransform must be MatrixTransform.");

        _imageLayer = this.FindControl<Canvas>("ImageLayer")
                      ?? throw new InvalidOperationException("ImageLayer not found.");

        ApplyTransform();
        CreateSelectionVisuals();

        _boardHost.KeyDown += OnBoardHostKeyDownImages;
        _boardHost.PointerWheelChanged += OnPointerWheelChanged;
        _boardHost.PointerPressed += OnPointerPressed;
        _boardHost.PointerMoved += OnPointerMoved;
        _boardHost.PointerReleased += OnPointerReleased;
        _boardHost.PointerCaptureLost += OnPointerCaptureLost;

        DragDrop.SetAllowDrop(_boardHost, true);
        _boardHost.AddHandler(DragDrop.DropEvent, OnBoardHostDropImages);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Enforce: screen = world * _zoom + _offset
    /// Matrix layout:
    ///   x' = x * M11 + y * M21 + M31
    ///   y' = x * M12 + y * M22 + M32
    /// so we set:
    ///   M11 = _zoom, M22 = _zoom, M31 = offset.X, M32 = offset.Y
    /// </summary>
    private void ApplyTransform()
    {
        _viewTransform.Matrix = new Matrix(
            _zoom,       // M11 = scaleX
            0,           // M12
            0,           // M21
            _zoom,       // M22 = scaleY
            _offset.X,   // M31 = translateX
            _offset.Y    // M32 = translateY
        );
    }

    private Point ScreenToWorld(Point screen)
    {
        return new Point(
            (screen.X - _offset.X) / _zoom,
            (screen.Y - _offset.Y) / _zoom
        );
    }

    // ---------- ZOOM: lock point under mouse ----------
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double oldZoom = _zoom;

        double factor = e.Delta.Y > 0
            ? (1.0 + ZoomFactorStep)
            : 1.0 / (1.0 + ZoomFactorStep);

        double newZoom = oldZoom * factor;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.0001)
            return;

        Point mouse = e.GetPosition(_boardHost);

        // World point under mouse BEFORE zoom:
        double worldX = (mouse.X - _offset.X) / oldZoom;
        double worldY = (mouse.Y - _offset.Y) / oldZoom;

        _zoom = newZoom;

        // New offset so that world point still maps to same screen position:
        _offset = new Vector(
            mouse.X - worldX * _zoom,
            mouse.Y - worldY * _zoom
        );

        ApplyTransform();
        e.Handled = true;
    }

    // ---------- PAN (middle mouse) + DESELECT (left click empty) ----------
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _boardHost.Focus();

        var pt = e.GetCurrentPoint(_boardHost);

        // Middle mouse → pan
        if (pt.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _lastPointerPos = pt.Position;
            _boardHost.Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(_boardHost);
            return;
        }

        // Left-click on empty background → deselect
        if (pt.Properties.IsLeftButtonPressed)
        {
            // Clicks on images/handles are handled in their own handlers (they mark e.Handled),
            // so this only runs when the click hits empty space.
            ClearSelection();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // --- Resizing image ---
        if (_isResizing && _selectedImage != null)
        {
            var screen = e.GetPosition(_boardHost);
            var world = ScreenToWorld(screen);
            ResizeSelectedImage(world);
            return;
        }

        // --- Dragging image ---
        if (_isDraggingImage && _selectedImage != null)
        {
            var screen = e.GetPosition(_boardHost);
            var world = ScreenToWorld(screen);
            var delta = world - _dragStartWorld;

            var newPos = new Point(
                _dragStartImageTopLeft.X + delta.X,
                _dragStartImageTopLeft.Y + delta.Y
            );

            Canvas.SetLeft(_selectedImage, newPos.X);
            Canvas.SetTop(_selectedImage, newPos.Y);
            UpdateSelectionVisuals();
            return;
        }

        // --- Panning ---
        if (!_isPanning)
            return;

        Point current = e.GetPosition(_boardHost);
        Vector panDelta = current - _lastPointerPos;
        _lastPointerPos = current;

        _offset += panDelta;
        ApplyTransform();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Stop resizing if active
        if (_isResizing)
        {
            _isResizing = false;
            _activeHandleIndex = -1;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
            return;
        }

        // Stop image dragging
        if (_isDraggingImage)
        {
            _isDraggingImage = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
            return;
        }

        // Stop panning
        if (_isPanning)
        {
            _isPanning = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            _activeHandleIndex = -1;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
        }

        if (_isDraggingImage)
        {
            _isDraggingImage = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
        }

        if (_isPanning)
        {
            _isPanning = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
        }
    }

    // =========================
    // IMAGE PASTE + DRAG/DROP
    // =========================

    // Ctrl+V handler
    private async void OnBoardHostKeyDownImages(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V &&
            (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            await PasteImageFromClipboardAsync();
            e.Handled = true;
        }
    }

    private async Task PasteImageFromClipboardAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        var clipboard = top?.Clipboard;
        if (clipboard is null)
            return;

        // 1) Try real bitmap from clipboard
        var bmp = await clipboard.TryGetBitmapAsync();
        if (bmp is not null)
        {
            var centerScreen = _boardHost.Bounds.Center;
            var centerWorld = ScreenToWorld(centerScreen);
            AddImageFromBitmap(bmp, centerWorld);
            return;
        }

        // 2) Fallback: text URL
        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!Uri.IsWellFormedUriString(text, UriKind.Absolute) ||
            !LooksLikeImageUrl(text))
            return;

        try
        {
            using var http = new HttpClient();
            await using var stream = await http.GetStreamAsync(text);
            var webBmp = new Bitmap(stream);

            var centerScreen = _boardHost.Bounds.Center;
            var centerWorld = ScreenToWorld(centerScreen);
            AddImageFromBitmap(webBmp, centerWorld);
        }
        catch
        {
            // ignore
        }
    }

    private static bool LooksLikeImageUrl(string url)
    {
        var lower = url.ToLowerInvariant();
        return lower.EndsWith(".png") ||
               lower.EndsWith(".jpg") ||
               lower.EndsWith(".jpeg") ||
               lower.EndsWith(".gif") ||
               lower.EndsWith(".webp");
    }

    // Drag & drop handler
    private async void OnBoardHostDropImages(object? sender, DragEventArgs e)
    {
        var data = e.Data;

        var screenPos = e.GetPosition(_boardHost);
        var worldPos = ScreenToWorld(screenPos);

        // 1) Local files
        if (data.Contains(DataFormats.FileNames))
        {
            var fileNames = data.GetFileNames();
            if (fileNames != null)
            {
                foreach (var path in fileNames)
                {
                    if (!IsImageFile(path))
                        continue;

                    try
                    {
                        await using var fs = File.OpenRead(path);
                        var bmp = new Bitmap(fs);
                        AddImageFromBitmap(bmp, worldPos);
                        worldPos = new Point(worldPos.X + 20, worldPos.Y + 20);
                    }
                    catch
                    {
                        // ignore single file errors
                    }
                }
            }

            e.Handled = true;
            return;
        }

        // 2) Text drop: treat as URL
        if (data.Contains(DataFormats.Text))
        {
            var text = data.GetText();
            if (!string.IsNullOrWhiteSpace(text) &&
                Uri.IsWellFormedUriString(text, UriKind.Absolute) &&
                LooksLikeImageUrl(text))
            {
                try
                {
                    using var http = new HttpClient();
                    await using var stream = await http.GetStreamAsync(text);
                    var bmp = new Bitmap(stream);
                    AddImageFromBitmap(bmp, worldPos);
                }
                catch
                {
                    // ignore
                }
            }

            e.Handled = true;
        }
    }

    private static bool IsImageFile(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.EndsWith(".png") ||
               lower.EndsWith(".jpg") ||
               lower.EndsWith(".jpeg") ||
               lower.EndsWith(".gif") ||
               lower.EndsWith(".bmp") ||
               lower.EndsWith(".webp");
    }

    // Actually add the image node to the canvas (in WORLD coords)
    private void AddImageFromBitmap(Bitmap bitmap, Point worldPos)
    {
        var img = new Image
        {
            Source = bitmap,
            Width = bitmap.PixelSize.Width,
            Height = bitmap.PixelSize.Height
        };

        Canvas.SetLeft(img, worldPos.X);
        Canvas.SetTop(img, worldPos.Y);

        img.PointerPressed += OnImagePointerPressed;

        _imageLayer.Children.Add(img);
    }

    // =========================
    // SELECTION + RESIZE + MOVE
    // =========================

    private void CreateSelectionVisuals()
    {
        // Outline
        _selectionOutline = new Border
        {
            BorderBrush = Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(2),
            Background = null,
            IsHitTestVisible = false
        };
        _selectionOutline.IsVisible = false;
        _imageLayer.Children.Add(_selectionOutline);

        // Corner handles (0=TL, 1=TR, 2=BR, 3=BL)
        for (int i = 0; i < 4; i++)
        {
            var handle = new Border
            {
                Width = 10,
                Height = 10,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            handle.Tag = i;
            handle.IsVisible = false;

            handle.PointerPressed += OnResizeHandlePointerPressed;
            handle.PointerEntered += OnResizeHandlePointerEntered;
            handle.PointerExited += OnResizeHandlePointerExited;

            _imageLayer.Children.Add(handle);
            _resizeHandles[i] = handle;
        }
    }

    private void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Image img)
            return;

        var point = e.GetCurrentPoint(img);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        SelectImage(img);

        // Start drag-move
        _isDraggingImage = true;
        _isResizing = false;
        _activeHandleIndex = -1;

        var screen = e.GetPosition(_boardHost);
        _dragStartWorld = ScreenToWorld(screen);
        _dragStartImageTopLeft = new Point(
            Canvas.GetLeft(img),
            Canvas.GetTop(img)
        );

        e.Pointer.Capture(_boardHost);
        e.Handled = true;
    }

    private void SelectImage(Image? img)
    {
        _selectedImage = img;
        UpdateSelectionVisuals();
    }

    private void ClearSelection()
    {
        _selectedImage = null;
        UpdateSelectionVisuals();
    }

    private Rect GetImageRect(Image img)
    {
        double left = Canvas.GetLeft(img);
        double top = Canvas.GetTop(img);
        double width = img.Width;
        double height = img.Height;
        return new Rect(left, top, width, height);
    }

    private void UpdateSelectionVisuals()
    {
        if (_selectionOutline == null)
            return;

        if (_selectedImage == null)
        {
            _selectionOutline.IsVisible = false;
            foreach (var h in _resizeHandles)
                if (h != null) h.IsVisible = false;
            return;
        }

        var rect = GetImageRect(_selectedImage);

        // Outline slightly bigger than image
        Canvas.SetLeft(_selectionOutline, rect.X - 2);
        Canvas.SetTop(_selectionOutline, rect.Y - 2);
        _selectionOutline.Width = rect.Width + 4;
        _selectionOutline.Height = rect.Height + 4;
        _selectionOutline.IsVisible = true;

        // Corner positions
        var corners = new[]
        {
            new Point(rect.X, rect.Y),                           // TL
            new Point(rect.X + rect.Width, rect.Y),              // TR
            new Point(rect.X + rect.Width, rect.Y + rect.Height),// BR
            new Point(rect.X, rect.Y + rect.Height)              // BL
        };

        for (int i = 0; i < 4; i++)
        {
            var handle = _resizeHandles[i];
            if (handle == null)
                continue;

            var c = corners[i];

            Canvas.SetLeft(handle, c.X - handle.Width / 2);
            Canvas.SetTop(handle, c.Y - handle.Height / 2);
            handle.IsVisible = true;
        }
    }

    private void OnResizeHandlePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Border)
            return;

        _boardHost.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void OnResizeHandlePointerExited(object? sender, PointerEventArgs e)
    {
        if (_isResizing)
            return;

        _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border handle || _selectedImage == null)
            return;

        var pt = e.GetCurrentPoint(handle);
        if (!pt.Properties.IsLeftButtonPressed)
            return;

        if (handle.Tag is not int index)
            return;

        _isResizing = true;
        _isDraggingImage = false;
        _activeHandleIndex = index;
        _originalImageRect = GetImageRect(_selectedImage);

        e.Pointer.Capture(_boardHost);
        e.Handled = true;
    }

    // Uniform (aspect-preserving) resize around opposite corner
    private void ResizeSelectedImage(Point pointerWorld)
    {
        if (_selectedImage == null || _activeHandleIndex < 0)
            return;

        var r = _originalImageRect;

        // Original corners in WORLD space
        var tl = new Point(r.X, r.Y);
        var tr = new Point(r.X + r.Width, r.Y);
        var br = new Point(r.X + r.Width, r.Y + r.Height);
        var bl = new Point(r.X, r.Y + r.Height);

        Point origCorner;
        Point oppCorner;

        switch (_activeHandleIndex)
        {
            case 0: // TL -> opposite = BR
                origCorner = tl;
                oppCorner = br;
                break;
            case 1: // TR -> opposite = BL
                origCorner = tr;
                oppCorner = bl;
                break;
            case 2: // BR -> opposite = TL
                origCorner = br;
                oppCorner = tl;
                break;
            case 3: // BL -> opposite = TR
                origCorner = bl;
                oppCorner = tr;
                break;
            default:
                return;
        }

        // Vector from opposite corner to original corner
        var v = new Vector(origCorner.X - oppCorner.X, origCorner.Y - oppCorner.Y);
        if (Math.Abs(v.X) < 1e-6 && Math.Abs(v.Y) < 1e-6)
            return;

        // Vector from opposite corner to pointer
        var p = new Vector(pointerWorld.X - oppCorner.X, pointerWorld.Y - oppCorner.Y);

        // Manual dot products
        double dot_vp = v.X * p.X + v.Y * p.Y;
        double dot_vv = v.X * v.X + v.Y * v.Y;

        double s = dot_vp / dot_vv;

        // Disallow flipping for now
        if (s <= 0)
            return;

        // Enforce minimum size while keeping aspect ratio
        const double minSize = 16;
        double minSide = Math.Min(r.Width, r.Height);
        if (minSide <= 0)
            return;

        double minScale = minSize / minSide;
        if (s < minScale)
            s = minScale;

        // New dragged corner = oppCorner + s * v
        var newCorner = new Point(
            oppCorner.X + v.X * s,
            oppCorner.Y + v.Y * s
        );

        double newLeft = Math.Min(oppCorner.X, newCorner.X);
        double newRight = Math.Max(oppCorner.X, newCorner.X);
        double newTop = Math.Min(oppCorner.Y, newCorner.Y);
        double newBottom = Math.Max(oppCorner.Y, newCorner.Y);

        double newWidth = newRight - newLeft;
        double newHeight = newBottom - newTop;

        Canvas.SetLeft(_selectedImage, newLeft);
        Canvas.SetTop(_selectedImage, newTop);
        _selectedImage.Width = newWidth;
        _selectedImage.Height = newHeight;

        UpdateSelectionVisuals();
    }
}
