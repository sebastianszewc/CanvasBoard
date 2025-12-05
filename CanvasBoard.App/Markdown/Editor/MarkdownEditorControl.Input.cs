using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CanvasBoard.App.Markdown.Document;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl
    {
        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            if (string.IsNullOrEmpty(e.Text))
                return;

            // Typing replaces current selection if any
            DeleteSelectionIfAny();

            Document.InsertText(e.Text);
            _text = Document.GetText();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            // --- Ctrl shortcuts: cut/copy/paste/select-all ---
            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.C:
                        CopySelectionOrAllToClipboardAsync();
                        e.Handled = true;
                        return;

                    case Key.X:
                        CutSelectionOrAllToClipboardAsync();
                        e.Handled = true;
                        return;

                    case Key.V:
                        PasteFromClipboardAsync();
                        e.Handled = true;
                        return;

                    case Key.A:
                        SelectAll();
                        _text = Document.GetText();
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                }
            }

            bool handled = true;

            switch (e.Key)
            {
                case Key.Left:
                    if (ctrl)
                    {
                        if (shift)
                        {
                            BeginSelectionIfNeeded();
                            Document.MoveCaretWordLeft();
                        }
                        else
                        {
                            Document.MoveCaretWordLeft();
                            ClearSelection();
                        }
                    }
                    else
                    {
                        if (shift)
                        {
                            BeginSelectionIfNeeded();
                            Document.MoveCaretLeft();
                        }
                        else
                        {
                            Document.MoveCaretLeft();
                            ClearSelection();
                        }
                    }
                    break;

                case Key.Right:
                    if (ctrl)
                    {
                        if (shift)
                        {
                            BeginSelectionIfNeeded();
                            Document.MoveCaretWordRight();
                        }
                        else
                        {
                            Document.MoveCaretWordRight();
                            ClearSelection();
                        }
                    }
                    else
                    {
                        if (shift)
                        {
                            BeginSelectionIfNeeded();
                            Document.MoveCaretRight();
                        }
                        else
                        {
                            Document.MoveCaretRight();
                            ClearSelection();
                        }
                    }
                    break;

                case Key.Up:
                    if (shift)
                    {
                        BeginSelectionIfNeeded();
                        MoveCaretVisual(-1);
                    }
                    else
                    {
                        MoveCaretVisual(-1);
                        ClearSelection();
                    }
                    break;

                case Key.Down:
                    if (shift)
                    {
                        BeginSelectionIfNeeded();
                        MoveCaretVisual(+1);
                    }
                    else
                    {
                        MoveCaretVisual(+1);
                        ClearSelection();
                    }
                    break;

                case Key.Home:
                    if (shift)
                    {
                        BeginSelectionIfNeeded();
                        Document.MoveCaretToLineStart();
                    }
                    else
                    {
                        Document.MoveCaretToLineStart();
                        ClearSelection();
                    }
                    break;

                case Key.End:
                    if (shift)
                    {
                        BeginSelectionIfNeeded();
                        Document.MoveCaretToLineEnd();
                    }
                    else
                    {
                        Document.MoveCaretToLineEnd();
                        ClearSelection();
                    }
                    break;

                case Key.Enter:
                    // Enter replaces selection if any
                    if (!DeleteSelectionIfAny())
                    {
                        // no selection
                    }
                    Document.InsertNewLine();
                    break;

                case Key.Back:
                    if (!DeleteSelectionIfAny())
                        Document.Backspace();
                    break;

                case Key.Delete:
                    if (!DeleteSelectionIfAny())
                        Document.Delete();
                    break;

                case Key.Tab:
                    // For now: insert spaces, not structural tab logic
                    DeleteSelectionIfAny();
                    Document.InsertText("    ");
                    break;

                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                _text = Document.GetText();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var p = e.GetPosition(this);
            EnsureLayout();

            if (_visualLines.Count == 0)
                return;

            int visIndex = (int)(p.Y / LineHeight);
            visIndex = System.Math.Clamp(visIndex, 0, _visualLines.Count - 1);

            var vis = _visualLines[visIndex];
            string rawLine = Document.Lines[vis.DocLineIndex] ?? string.Empty;

            int colIndex = GetColumnFromX(rawLine, vis.StartColumn, vis.Length, p.X);
            Document.SetCaret(vis.DocLineIndex, colIndex);

            // Start new selection on left button
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed)
            {
                _mouseSelecting = true;
                _selectionAnchorLine = Document.CaretLine;
                _selectionAnchorColumn = Document.CaretColumn;
                _hasSelection = false;
            }
            else
            {
                // Any non-left click clears selection
                ClearSelection();
            }

            _text = Document.GetText();
            InvalidateVisual();
            e.Handled = true;
            Focus();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!_mouseSelecting)
                return;

            var p = e.GetPosition(this);
            EnsureLayout();

            if (_visualLines.Count == 0)
                return;

            int visIndex = (int)(p.Y / LineHeight);
            visIndex = System.Math.Clamp(visIndex, 0, _visualLines.Count - 1);

            var vis = _visualLines[visIndex];
            string rawLine = Document.Lines[vis.DocLineIndex] ?? string.Empty;

            int colIndex = GetColumnFromX(rawLine, vis.StartColumn, vis.Length, p.X);
            Document.SetCaret(vis.DocLineIndex, colIndex);

            // Activate selection if caret moved away from anchor
            BeginSelectionIfNeeded();

            _text = Document.GetText();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            _mouseSelecting = false;
        }

        private int GetColumnFromX(string rawLine, int startColumn, int length, double x)
        {
            double localX = x - LeftPadding;
            if (localX <= 0)
                return startColumn;

            double cw = GetCharWidth();
            if (cw <= 0)
                return startColumn;

            int maxCol = System.Math.Min(rawLine.Length, startColumn + length);
            if (maxCol <= startColumn)
                return startColumn;

            // How many characters from startColumn does this x correspond to?
            int offsetChars = (int)System.Math.Round(localX / cw);
            int col = startColumn + offsetChars;

            // Clamp into [startColumn, maxCol]
            if (col < startColumn) col = startColumn;
            if (col > maxCol) col = maxCol;

            return col;
        }

        // Move caret vertically between visual lines (wrapped segments)
        private void MoveCaretVisual(int delta)
        {
            if (delta == 0 || _visualLines.Count == 0)
                return;

            EnsureLayout();

            int caretLine = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            // Find current visual line for the caret
            int currentVisIndex = -1;
            VisualLine? currentVis = null;

            for (int i = 0; i < _visualLines.Count; i++)
            {
                var v = _visualLines[i];
                if (v.DocLineIndex != caretLine)
                    continue;

                int start = v.StartColumn;
                int end = start + v.Length;

                // If line is empty, treat it as a single segment
                if (v.Length == 0)
                {
                    currentVisIndex = i;
                    currentVis = v;
                    break;
                }

                if (caretCol >= start && caretCol <= end)
                {
                    currentVisIndex = i;
                    currentVis = v;
                    break;
                }
            }

            // If we didn't find an exact segment, fall back to first segment of that line
            if (currentVis == null)
            {
                for (int i = 0; i < _visualLines.Count; i++)
                {
                    var v = _visualLines[i];
                    if (v.DocLineIndex == caretLine)
                    {
                        currentVisIndex = i;
                        currentVis = v;
                        break;
                    }
                }

                if (currentVis == null)
                    return;
            }

            // Horizontal offset in characters from the visual segment start
            int offsetInSegment = caretCol - currentVis.StartColumn;
            if (offsetInSegment < 0)
                offsetInSegment = 0;
            if (offsetInSegment > currentVis.Length)
                offsetInSegment = currentVis.Length;

            int targetIndex = currentVisIndex + delta;
            if (targetIndex < 0 || targetIndex >= _visualLines.Count)
                return; // nothing above/below

            var targetVis = _visualLines[targetIndex];

            // Clamp offset to the length of the target segment
            int newOffset = offsetInSegment;
            if (newOffset > targetVis.Length)
                newOffset = targetVis.Length;

            int newCol = targetVis.StartColumn + newOffset;
            string targetLine = Document.Lines[targetVis.DocLineIndex] ?? string.Empty;
            newCol = System.Math.Clamp(newCol, 0, targetLine.Length);

            Document.SetCaret(targetVis.DocLineIndex, newCol);
        }

        private void MoveCaretToEndOfDocument()
        {
            int lastLine = Document.Lines.Count - 1;
            if (lastLine < 0)
                return;

            int lastCol = Document.Lines[lastLine]?.Length ?? 0;
            Document.SetCaret(lastLine, lastCol);
        }

        private void SelectAll()
        {
            if (Document.Lines.Count == 0)
                return;

            _selectionAnchorLine = 0;
            _selectionAnchorColumn = 0;

            int lastLine = Document.Lines.Count - 1;
            int lastCol = Document.Lines[lastLine]?.Length ?? 0;
            Document.SetCaret(lastLine, lastCol);

            _hasSelection = true;
        }

        // --- Clipboard helpers (async) ---

        private async void CopySelectionOrAllToClipboardAsync()
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null)
                return;

            string text;
            if (TryGetSelectionSpan(out var span))
                text = Document.GetText(span);
            else
                text = _text ?? string.Empty;

            await top.Clipboard.SetTextAsync(text);
        }

        private async void CutSelectionOrAllToClipboardAsync()
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null)
                return;

            string text;
            if (TryGetSelectionSpan(out var span))
                text = Document.GetText(span);
            else
                text = _text ?? string.Empty;

            await top.Clipboard.SetTextAsync(text);

            if (TryGetSelectionSpan(out var span2))
            {
                Document.DeleteSpan(span2);
                Document.SetCaret(span2.StartLine, span2.StartColumn);
                ClearSelection();
            }
            else
            {
                Document.SetText(string.Empty);
                ClearSelection();
            }

            _text = Document.GetText();
            InvalidateVisual();
        }

        private async void PasteFromClipboardAsync()
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is null)
                return;

            var text = await top.Clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(text))
                return;

            // Replace selection if any
            DeleteSelectionIfAny();

            Document.InsertTextWithNewlines(text);
            _text = Document.GetText();
            InvalidateVisual();
        }
    }
}
