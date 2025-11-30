using System;
using System.Collections.Generic;
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

    // Multi-selection state
    private readonly HashSet<Image> _selectedImages = new();
    private Border? _selectionOutline;
    private readonly Border[] _resizeHandles = new Border[4];

    private bool _isResizingSelection;
    private int _activeHandleIndex = -1;

    // For group resize
    private Rect _originalGroupRect;
    private Dictionary<Image, Rect> _originalImageRects = new();

    // Drag-move selection
    private bool _isDraggingSelection;
    private Point _dragStartWorld;
    private Dictionary<Image, Point> _dragStartImageTopLefts = new();

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

    // ============= TRANSFORM =============

    /// <summary>
    /// screen = world * _zoom + _offset
    /// x' = x * M11 + y * M21 + M31
    /// y' = x * M12 + y * M22 + M32
    /// </summary>
    private void ApplyTransform()
    {
        _viewTransform.Matrix = new Matrix(
            _zoom,       // M11
            0,           // M12
            0,           // M21
            _zoom,       // M22
            _offset.X,   // M31
            _offset.Y    // M32
        );
    }

    private Point ScreenToWorld(Point screen)
    {
        return new Point(
            (screen.X - _offset.X) / _zoom,
            (screen.Y - _offset.Y) / _zoom
        );
    }

    // ============= ZOOM / PAN / EMPTY-CLICK DESELECT =============

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double oldZoom = _zoom;

        double factor = e.Delta.Y > 0
            ? (1.0 + ZoomFactorStep)
            : 1.0 / (1.0 + ZoomFactorStep);

        double newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001)
            return;

        Point mouse = e.GetPosition(_boardHost);

        double worldX = (mouse.X - _offset.X) / oldZoom;
        double worldY = (mouse.Y - _offset.Y) / oldZoom;

        _zoom = newZoom;

        _offset = new Vector(
            mouse.X - worldX * _zoom,
            mouse.Y - worldY * _zoom
        );

        ApplyTransform();
        e.Handled = true;
    }

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

        // Left-click on empty background → deselect.
        // Clicks on images/handles are handled by their own handlers and mark e.Handled,
        // so this only runs when we hit empty space.
        if (pt.Properties.IsLeftButtonPressed && !e.Handled)
        {
            ClearSelection();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // --- Resizing selection ---
        if (_isResizingSelection && _selectedImages.Count > 0)
        {
            var screen = e.GetPosition(_boardHost);
            var world = ScreenToWorld(screen);
            ResizeSelection(world);
            return;
        }

        // --- Dragging selection ---
        if (_isDraggingSelection && _selectedImages.Count > 0)
        {
            var screen = e.GetPosition(_boardHost);
            var world = ScreenToWorld(screen);
            var delta = world - _dragStartWorld;

            foreach (var kvp in _dragStartImageTopLefts)
            {
                var img = kvp.Key;
                var startPos = kvp.Value;
                var newPos = new Point(startPos.X + delta.X, startPos.Y + delta.Y);
                Canvas.SetLeft(img, newPos.X);
                Canvas.SetTop(img, newPos.Y);
            }

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
        if (_isResizingSelection)
        {
            _isResizingSelection = false;
            _activeHandleIndex = -1;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
            return;
        }

        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isResizingSelection)
        {
            _isResizingSelection = false;
            _activeHandleIndex = -1;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
        }

        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
        }

        if (_isPanning)
        {
            _isPanning = false;
            _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
        }
    }

    // ============= CLIPBOARD: CTRL+V =============

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

        // Try real bitmap from clipboard
        var bmp = await clipboard.TryGetBitmapAsync();
        if (bmp is not null)
        {
            var centerScreen = _boardHost.Bounds.Center;
            var centerWorld = ScreenToWorld(centerScreen);
            AddImageFromBitmap(bmp, centerWorld);
            return;
        }

        // Fallback: text URL
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

    // ============= DRAG & DROP =============

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

        // 2) Text drop: URL
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

    // ============= MULTI-SELECTION + RESIZE + MOVE =============

    private void CreateSelectionVisuals()
    {
        _selectionOutline = new Border
        {
            BorderBrush = Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(2),
            Background = null,
            IsHitTestVisible = false
        };
        _selectionOutline.IsVisible = false;
        _imageLayer.Children.Add(_selectionOutline);

        // 0=TL, 1=TR, 2=BR, 3=BL
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

        var mods = e.KeyModifiers;

        if ((mods & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            // SHIFT: toggle image in selection
            if (_selectedImages.Contains(img))
                _selectedImages.Remove(img);
            else
                _selectedImages.Add(img);

            if (_selectedImages.Count == 0)
                ClearSelection();
            else
                UpdateSelectionVisuals();
        }
        else
        {
            // No shift:
            if (!_selectedImages.Contains(img))
            {
                // Clicked outside current group: switch to single selection
                _selectedImages.Clear();
                _selectedImages.Add(img);
            }
            // If it's already in the group, keep the whole selection as-is
            UpdateSelectionVisuals();
        }

        // Start drag-move of the whole selection
        if (_selectedImages.Count > 0)
        {
            _isDraggingSelection = true;
            _isResizingSelection = false;
            _activeHandleIndex = -1;

            var screen = e.GetPosition(_boardHost);
            _dragStartWorld = ScreenToWorld(screen);

            _dragStartImageTopLefts = new Dictionary<Image, Point>();
            foreach (var sel in _selectedImages)
            {
                _dragStartImageTopLefts[sel] = new Point(
                    Canvas.GetLeft(sel),
                    Canvas.GetTop(sel)
                );
            }

            e.Pointer.Capture(_boardHost);
        }

        e.Handled = true;
    }

    private void ClearSelection()
    {
        _selectedImages.Clear();
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

    private Rect GetGroupRect()
    {
        bool first = true;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;

        foreach (var img in _selectedImages)
        {
            var r = GetImageRect(img);
            if (first)
            {
                first = false;
                minX = r.X;
                minY = r.Y;
                maxX = r.X + r.Width;
                maxY = r.Y + r.Height;
            }
            else
            {
                minX = Math.Min(minX, r.X);
                minY = Math.Min(minY, r.Y);
                maxX = Math.Max(maxX, r.X + r.Width);
                maxY = Math.Max(maxY, r.Y + r.Height);
            }
        }

        if (first)
            return new Rect(0, 0, 0, 0);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private void UpdateSelectionVisuals()
    {
        if (_selectionOutline == null)
            return;

        if (_selectedImages.Count == 0)
        {
            _selectionOutline.IsVisible = false;
            foreach (var h in _resizeHandles)
                if (h != null) h.IsVisible = false;
            return;
        }

        var rect = GetGroupRect();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            _selectionOutline.IsVisible = false;
            foreach (var h in _resizeHandles)
                if (h != null) h.IsVisible = false;
            return;
        }


        Canvas.SetLeft(_selectionOutline, rect.X - 2);
        Canvas.SetTop(_selectionOutline, rect.Y - 2);
        _selectionOutline.Width = rect.Width + 4;
        _selectionOutline.Height = rect.Height + 4;
        _selectionOutline.IsVisible = true;

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
        if (_isResizingSelection)
            return;

        _boardHost.Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border handle)
            return;
        if (_selectedImages.Count == 0)
            return;

        var pt = e.GetCurrentPoint(handle);
        if (!pt.Properties.IsLeftButtonPressed)
            return;

        if (handle.Tag is not int index)
            return;

        _isResizingSelection = true;
        _isDraggingSelection = false;
        _activeHandleIndex = index;

        _originalGroupRect = GetGroupRect();
        _originalImageRects = new Dictionary<Image, Rect>();
        foreach (var img in _selectedImages)
            _originalImageRects[img] = GetImageRect(img);

        e.Pointer.Capture(_boardHost);
        e.Handled = true;
    }

    /// <summary>
    /// Uniform (aspect-preserving) resize of the whole selection around opposite group corner.
    /// </summary>
    private void ResizeSelection(Point pointerWorld)
    {
        if (_selectedImages.Count == 0 || _activeHandleIndex < 0)
            return;

        var r = _originalGroupRect;
        if (r.Width <= 0 || r.Height <= 0)
            return;

        // Original group corners
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

        var v = new Vector(origCorner.X - oppCorner.X, origCorner.Y - oppCorner.Y);
        if (Math.Abs(v.X) < 1e-6 && Math.Abs(v.Y) < 1e-6)
            return;

        var p = new Vector(pointerWorld.X - oppCorner.X, pointerWorld.Y - oppCorner.Y);

        double dot_vp = v.X * p.X + v.Y * p.Y;
        double dot_vv = v.X * v.X + v.Y * v.Y;

        double s = dot_vp / dot_vv;
        if (s <= 0)
            return;

        const double minSize = 16;
        double minSide = Math.Min(r.Width, r.Height);
        if (minSide <= 0)
            return;

        double minScale = minSize / minSide;
        if (s < minScale)
            s = minScale;

        // For each image, scale its original rect about the same oppCorner
        foreach (var kvp in _originalImageRects)
        {
            var img = kvp.Key;
            var imgRect = kvp.Value;

            var tlImg = new Point(imgRect.X, imgRect.Y);
            var brImg = new Point(imgRect.X + imgRect.Width, imgRect.Y + imgRect.Height);

            var vTL = new Vector(tlImg.X - oppCorner.X, tlImg.Y - oppCorner.Y);
            var vBR = new Vector(brImg.X - oppCorner.X, brImg.Y - oppCorner.Y);

            var newTL = new Point(
                oppCorner.X + vTL.X * s,
                oppCorner.Y + vTL.Y * s
            );
            var newBR = new Point(
                oppCorner.X + vBR.X * s,
                oppCorner.Y + vBR.Y * s
            );

            double newLeft = Math.Min(newTL.X, newBR.X);
            double newTop = Math.Min(newTL.Y, newBR.Y);
            double newRight = Math.Max(newTL.X, newBR.X);
            double newBottom = Math.Max(newTL.Y, newBR.Y);

            double newWidth = newRight - newLeft;
            double newHeight = newBottom - newTop;

            // Avoid degenerate
            if (newWidth < 1 || newHeight < 1)
                continue;

            Canvas.SetLeft(img, newLeft);
            Canvas.SetTop(img, newTop);
            img.Width = newWidth;
            img.Height = newHeight;
        }

        UpdateSelectionVisuals();
    }
}
