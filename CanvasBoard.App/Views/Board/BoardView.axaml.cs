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
    private Canvas _noteLayer = null!;

    // world -> screen:  screen = world * _zoom + _offset
    private double _zoom = 1.0;
    private Vector _offset = new Vector(0, 0);

    private bool _isPanning;
    private Point _lastPointerPos;

    private const double MinZoom = 0.1;
    private const double MaxZoom = 4.0;
    private const double ZoomFactorStep = 0.1; // 10% per wheel tick

    // Selection state (images + notes)
    private readonly HashSet<Image> _selectedImages = new();
    private readonly HashSet<NoteView> _selectedNotes = new();

    private Border? _selectionOutline;
    private readonly Border[] _resizeHandles = new Border[4];

    private bool _isResizingSelection;
    private int _activeHandleIndex = -1;

    private Rect _originalGroupRect;
    private Dictionary<Image, Rect> _originalImageRects = new();
    private Dictionary<NoteView, Rect> _originalNoteRects = new();

    // Drag-move selection
    private bool _isDraggingSelection;
    private Point _dragStartWorld;
    private Dictionary<Image, Point> _dragStartImageTopLefts = new();
    private Dictionary<NoteView, Point> _dragStartNoteTopLefts = new();

    // Z-order
    private int _zCounter = 0;

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
        _noteLayer = this.FindControl<Canvas>("NoteLayer")
                     ?? throw new InvalidOperationException("NoteLayer not found.");

        ApplyTransform();
        CreateSelectionVisuals();

        _boardHost.KeyDown += OnBoardHostKeyDown;
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

    private void ApplyTransform()
    {
        // screen = world * _zoom + _offset
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

        // Left-click on empty background → deselect (clicks on nodes mark e.Handled)
        if (pt.Properties.IsLeftButtonPressed && !e.Handled)
        {
            ClearSelection();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // --- Resizing selection ---
        if (_isResizingSelection && (_selectedImages.Count > 0 || _selectedNotes.Count > 0))
        {
            var screen = e.GetPosition(_boardHost);
            var world = ScreenToWorld(screen);
            ResizeSelection(world);
            return;
        }

        // --- Dragging selection ---
        if (_isDraggingSelection && (_selectedImages.Count > 0 || _selectedNotes.Count > 0))
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

            foreach (var kvp in _dragStartNoteTopLefts)
            {
                var note = kvp.Key;
                var startPos = kvp.Value;
                var newPos = new Point(startPos.X + delta.X, startPos.Y + delta.Y);
                Canvas.SetLeft(note, newPos.X);
                Canvas.SetTop(note, newPos.Y);
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

    // ============= KEYBOARD: CTRL+V (image), CTRL+N (note) =============

    private async void OnBoardHostKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            if (e.Key == Key.V)
            {
                await PasteImageFromClipboardAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.N)
            {
                AddNoteAtViewCenter();
                e.Handled = true;
                return;
            }
        }
    }

    // ============= CLIPBOARD: CTRL+V IMAGE =============

    private async Task PasteImageFromClipboardAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        var clipboard = top?.Clipboard;
        if (clipboard is null)
            return;

        // Try bitmap
        var bmp = await clipboard.TryGetBitmapAsync();
        if (bmp is not null)
        {
            var centerScreen = _boardHost.Bounds.Center;
            var centerWorld = ScreenToWorld(centerScreen);
            AddImageFromBitmap(bmp, centerWorld);
            return;
        }

        // Fallback: image URL
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

    // ============= DRAG & DROP IMAGES =============

    private async void OnBoardHostDropImages(object? sender, DragEventArgs e)
    {
        var data = e.Data;

        var screenPos = e.GetPosition(_boardHost);
        var worldPos = ScreenToWorld(screenPos);

        // Local files
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
                        // ignore
                    }
                }
            }

            e.Handled = true;
            return;
        }

        // Text drop: URL
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

    // ============= IMAGE & NOTE CREATION =============

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

        img.ZIndex = _zCounter++;

        img.PointerPressed += OnImagePointerPressed;

        _imageLayer.Children.Add(img);
    }

    private void AddNoteAtViewCenter()
    {
        var centerScreen = _boardHost.Bounds.Center;
        var centerWorld = ScreenToWorld(centerScreen);

        var note = new NoteView
        {
            Width = 300,
            Height = 200,
            Text = "## New note\nEdit me."
        };

        var pos = new Point(centerWorld.X - note.Width / 2, centerWorld.Y - note.Height / 2);
        Canvas.SetLeft(note, pos.X);
        Canvas.SetTop(note, pos.Y);

        note.ZIndex = _zCounter++;

        note.PointerPressed += OnNotePointerPressed;

        _noteLayer.Children.Add(note);
    }

    // ============= SELECTION HELPERS =============

    private void ClearSelection()
    {
        _selectedImages.Clear();
        _selectedNotes.Clear();
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

    private Rect GetNoteRect(NoteView note)
    {
        double left = Canvas.GetLeft(note);
        double top = Canvas.GetTop(note);
        double width = note.Width;
        double height = note.Height;
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

        foreach (var note in _selectedNotes)
        {
            var r = GetNoteRect(note);
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

        if (_selectedImages.Count == 0 && _selectedNotes.Count == 0)
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

    private void BringSelectionToFront()
    {
        foreach (var img in _selectedImages)
            img.ZIndex = _zCounter++;

        foreach (var note in _selectedNotes)
            note.ZIndex = _zCounter++;

        if (_selectionOutline != null)
            _selectionOutline.ZIndex = int.MaxValue - 1;

        foreach (var h in _resizeHandles)
            if (h != null)
                h.ZIndex = int.MaxValue;
    }

    // ============= IMAGE CLICK / SELECTION =============

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
            if (_selectedImages.Contains(img))
                _selectedImages.Remove(img);
            else
                _selectedImages.Add(img);

            if (_selectedImages.Count == 0 && _selectedNotes.Count == 0)
            {
                ClearSelection();
            }
            else
            {
                BringSelectionToFront();
                UpdateSelectionVisuals();
            }
        }
        else
        {
            if (!_selectedImages.Contains(img) || _selectedNotes.Count > 0)
            {
                _selectedImages.Clear();
                _selectedNotes.Clear();
                _selectedImages.Add(img);
            }

            if (_selectedImages.Count > 0 || _selectedNotes.Count > 0)
                BringSelectionToFront();

            UpdateSelectionVisuals();
        }

        if (_selectedImages.Count > 0 || _selectedNotes.Count > 0)
        {
            _isDraggingSelection = true;
            _isResizingSelection = false;
            _activeHandleIndex = -1;

            var screen = e.GetPosition(_boardHost);
            _dragStartWorld = ScreenToWorld(screen);

            _dragStartImageTopLefts = new Dictionary<Image, Point>();
            _dragStartNoteTopLefts = new Dictionary<NoteView, Point>();

            foreach (var sel in _selectedImages)
            {
                _dragStartImageTopLefts[sel] = new Point(
                    Canvas.GetLeft(sel),
                    Canvas.GetTop(sel)
                );
            }

            foreach (var sel in _selectedNotes)
            {
                _dragStartNoteTopLefts[sel] = new Point(
                    Canvas.GetLeft(sel),
                    Canvas.GetTop(sel)
                );
            }

            e.Pointer.Capture(_boardHost);
        }

        e.Handled = true;
    }

    // ============= NOTE CLICK / SELECTION =============

    private void OnNotePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not NoteView note)
            return;

        // If the note is currently being edited, ignore this so text editing works normally
        if (note.IsEditing)
            return;

        var point = e.GetCurrentPoint(note);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var mods = e.KeyModifiers;

        if ((mods & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            if (_selectedNotes.Contains(note))
                _selectedNotes.Remove(note);
            else
                _selectedNotes.Add(note);

            if (_selectedImages.Count == 0 && _selectedNotes.Count == 0)
            {
                ClearSelection();
            }
            else
            {
                BringSelectionToFront();
                UpdateSelectionVisuals();
            }
        }
        else
        {
            if (!_selectedNotes.Contains(note) || _selectedImages.Count > 0)
            {
                _selectedImages.Clear();
                _selectedNotes.Clear();
                _selectedNotes.Add(note);
            }

            if (_selectedImages.Count > 0 || _selectedNotes.Count > 0)
                BringSelectionToFront();

            UpdateSelectionVisuals();
        }

        if (_selectedImages.Count > 0 || _selectedNotes.Count > 0)
        {
            _isDraggingSelection = true;
            _isResizingSelection = false;
            _activeHandleIndex = -1;

            var screen = e.GetPosition(_boardHost);
            _dragStartWorld = ScreenToWorld(screen);

            _dragStartImageTopLefts = new Dictionary<Image, Point>();
            _dragStartNoteTopLefts = new Dictionary<NoteView, Point>();

            foreach (var sel in _selectedImages)
            {
                _dragStartImageTopLefts[sel] = new Point(
                    Canvas.GetLeft(sel),
                    Canvas.GetTop(sel)
                );
            }

            foreach (var sel in _selectedNotes)
            {
                _dragStartNoteTopLefts[sel] = new Point(
                    Canvas.GetLeft(sel),
                    Canvas.GetTop(sel)
                );
            }

            e.Pointer.Capture(_boardHost);
        }

        e.Handled = true;
    }

    // ============= RESIZE HANDLES (GROUP) =============

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
        if (_selectedImages.Count == 0 && _selectedNotes.Count == 0)
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
        _originalNoteRects = new Dictionary<NoteView, Rect>();

        foreach (var img in _selectedImages)
            _originalImageRects[img] = GetImageRect(img);

        foreach (var note in _selectedNotes)
            _originalNoteRects[note] = GetNoteRect(note);

        e.Pointer.Capture(_boardHost);
        e.Handled = true;
    }

    private void ResizeSelection(Point pointerWorld)
    {
        if (_selectedImages.Count == 0 && _selectedNotes.Count == 0)
            return;
        if (_activeHandleIndex < 0)
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

        // Vector from opposite corner to original corner (for uniform image scale)
        var v = new Vector(origCorner.X - oppCorner.X, origCorner.Y - oppCorner.Y);
        if (Math.Abs(v.X) < 1e-6 && Math.Abs(v.Y) < 1e-6)
            return;

        // Vector from opposite corner to pointer
        var p = new Vector(pointerWorld.X - oppCorner.X, pointerWorld.Y - oppCorner.Y);

        // Uniform scalar for images
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

        // --- Anisotropic factors for notes (sx, sy) ---
        double dxOrig = origCorner.X - oppCorner.X;
        double dyOrig = origCorner.Y - oppCorner.Y;
        double dxPtr = pointerWorld.X - oppCorner.X;
        double dyPtr = pointerWorld.Y - oppCorner.Y;

        double sx, sy;

        if (Math.Abs(dxOrig) < 1e-6)
            sx = s;                        // fallback to uniform if no horizontal span
        else
            sx = dxPtr / dxOrig;

        if (Math.Abs(dyOrig) < 1e-6)
            sy = s;                        // fallback to uniform if no vertical span
        else
            sy = dyPtr / dyOrig;

        // Prevent flipping and tiny sizes
        if (sx <= 0) sx = minScale;
        if (sy <= 0) sy = minScale;

        sx = Math.Max(sx, minScale);
        sy = Math.Max(sy, minScale);

        // ---------- IMAGES: uniform scale (s) ----------
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

            if (newWidth < 1 || newHeight < 1)
                continue;

            Canvas.SetLeft(img, newLeft);
            Canvas.SetTop(img, newTop);
            img.Width = newWidth;
            img.Height = newHeight;
        }

        // ---------- NOTES: non-uniform scale (sx, sy) ----------
        foreach (var kvp in _originalNoteRects)
        {
            var note = kvp.Key;
            var noteRect = kvp.Value;

            var tlNote = new Point(noteRect.X, noteRect.Y);
            var brNote = new Point(noteRect.X + noteRect.Width, noteRect.Y + noteRect.Height);

            var vTL = new Vector(tlNote.X - oppCorner.X, tlNote.Y - oppCorner.Y);
            var vBR = new Vector(brNote.X - oppCorner.X, brNote.Y - oppCorner.Y);

            var newTL = new Point(
                oppCorner.X + vTL.X * sx,
                oppCorner.Y + vTL.Y * sy
            );
            var newBR = new Point(
                oppCorner.X + vBR.X * sx,
                oppCorner.Y + vBR.Y * sy
            );

            double newLeft = Math.Min(newTL.X, newBR.X);
            double newTop = Math.Min(newTL.Y, newBR.Y);
            double newRight = Math.Max(newTL.X, newBR.X);
            double newBottom = Math.Max(newTL.Y, newBR.Y);

            double newWidth = newRight - newLeft;
            double newHeight = newBottom - newTop;

            if (newWidth < 1 || newHeight < 1)
                continue;

            Canvas.SetLeft(note, newLeft);
            Canvas.SetTop(note, newTop);
            note.Width = newWidth;
            note.Height = newHeight;
        }

        UpdateSelectionVisuals();
    }

}
