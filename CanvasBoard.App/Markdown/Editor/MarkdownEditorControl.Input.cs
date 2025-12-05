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

            // Regular typing: Replace selection (if any) with e.Text
            LineSpan range;
            if (TryGetSelectionSpan(out var selSpan))
            {
                range = selSpan;
            }
            else
            {
                range = new LineSpan(
                    Document.CaretLine,
                    Document.CaretColumn,
                    Document.CaretLine,
                    Document.CaretColumn);
            }

            var op = ReplaceRangeOperation.CreateAndApply(
                Document,
                this,
                range,
                e.Text,
                clearSelectionAfter: true);

            PushOperation(op, allowMerge: true);

            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            // --- Ctrl shortcuts: undo/redo/cut/copy/paste/select-all ---
            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.Z:
                        if (shift)
                            Redo();
                        else
                            Undo();
                        e.Handled = true;
                        return;

                    case Key.Y:
                        Redo();
                        e.Handled = true;
                        return;

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
                {
                    // Replace selection (if any) with newline
                    LineSpan range;
                    if (TryGetSelectionSpan(out var selSpan))
                    {
                        range = selSpan;
                    }
                    else
                    {
                        range = new LineSpan(
                            Document.CaretLine,
                            Document.CaretColumn,
                            Document.CaretLine,
                            Document.CaretColumn);
                    }

                    var op = ReplaceRangeOperation.CreateAndApply(
                        Document,
                        this,
                        range,
                        "\n",
                        clearSelectionAfter: true);

                    PushOperation(op, allowMerge: false);
                    break;
                }

                case Key.Back:
                {
                    // If selection -> delete selection; else delete char before caret (including newline)
                    if (TryGetSelectionSpan(out var selSpan))
                    {
                        var op = ReplaceRangeOperation.CreateAndApply(
                            Document,
                            this,
                            selSpan,
                            string.Empty,
                            clearSelectionAfter: true);

                        PushOperation(op, allowMerge: false);
                    }
                    else
                    {
                        int line = Document.CaretLine;
                        int col = Document.CaretColumn;

                        if (line == 0 && col == 0)
                            break; // nothing to delete

                        int startLine, startCol, endLine, endCol;

                        if (col > 0)
                        {
                            startLine = line;
                            startCol = col - 1;
                            endLine = line;
                            endCol = col;
                        }
                        else
                        {
                            // At column 0, delete the "newline" joining previous line
                            startLine = line - 1;
                            startCol = (Document.Lines[startLine] ?? string.Empty).Length;
                            endLine = line;
                            endCol = 0;
                        }

                        var range = new LineSpan(startLine, startCol, endLine, endCol);

                        var op = ReplaceRangeOperation.CreateAndApply(
                            Document,
                            this,
                            range,
                            string.Empty,
                            clearSelectionAfter: true);

                        PushOperation(op, allowMerge: false);
                    }
                    break;
                }

                case Key.Delete:
                {
                    // If selection -> delete selection; else delete char after caret (including newline)
                    if (TryGetSelectionSpan(out var selSpan))
                    {
                        var op = ReplaceRangeOperation.CreateAndApply(
                            Document,
                            this,
                            selSpan,
                            string.Empty,
                            clearSelectionAfter: true);

                        PushOperation(op, allowMerge: false);
                    }
                    else
                    {
                        int line = Document.CaretLine;
                        int col = Document.CaretColumn;
                        string lineText = Document.Lines[line] ?? string.Empty;

                        int startLine = line, startCol = col, endLine = line, endCol = col;

                        if (col < lineText.Length)
                        {
                            // Delete character inside the line
                            startLine = line;
                            startCol = col;
                            endLine = line;
                            endCol = col + 1;
                        }
                        else if (line + 1 < Document.Lines.Count)
                        {
                            // Delete the "newline" joining this and next line
                            startLine = line;
                            startCol = col;
                            endLine = line + 1;
                            endCol = 0;
                        }
                        else
                        {
                            break; // nothing to delete
                        }

                        var range = new LineSpan(startLine, startCol, endLine, endCol);

                        var op = ReplaceRangeOperation.CreateAndApply(
                            Document,
                            this,
                            range,
                            string.Empty,
                            clearSelectionAfter: true);

                        PushOperation(op, allowMerge: false);
                    }
                    break;
                }

                case Key.Tab:
                {
                    // Indent/outdent current line or all selected lines; treat as one snapshot op.
                    int firstLine, lastLine;
                    if (TryGetSelectionSpan(out var selSpan))
                    {
                        firstLine = selSpan.StartLine;
                        lastLine = selSpan.EndLine;
                    }
                    else
                    {
                        firstLine = lastLine = Document.CaretLine;
                    }

                    var beforeSel = CaptureSelectionState();
                    var beforeText = Document.GetText();

                    if (shift)
                        OutdentLines(firstLine, lastLine);
                    else
                        IndentLines(firstLine, lastLine);

                    _text = Document.GetText();
                    var afterSel = CaptureSelectionState();
                    var afterText = _text;

                    var op = new SnapshotOperation(beforeText, afterText, beforeSel, afterSel);
                    PushOperation(op, allowMerge: false);

                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

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

            int maxCharIndex = System.Math.Min(rawLine.Length, startColumn + length);

            // Desired visual column
            int targetCols = (int)System.Math.Round(localX / cw);
            if (targetCols <= 0)
                return startColumn;

            int col = 0;
            int index = startColumn;

            while (index < maxCharIndex)
            {
                char ch = rawLine[index];

                int nextCol;
                if (ch == '\t')
                {
                    nextCol = ((col / TabSize) + 1) * TabSize;
                }
                else
                {
                    nextCol = col + 1;
                }

                if (nextCol >= targetCols)
                {
                    // Decide whether to snap before or after this character
                    int mid = (col + nextCol) / 2;
                    if (targetCols <= mid)
                        return index;       // before this char
                    else
                        return index + 1;   // after this char
                }

                col = nextCol;
                index++;
            }

            return maxCharIndex;
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

            // Current line text (where the caret is now)
            string currentLineText = Document.Lines[caretLine] ?? string.Empty;

            // Visual columns from segment start to caret, on the current line
            int currentCols = ComputeColumns(
                currentLineText,
                currentVis.StartColumn,
                caretCol - currentVis.StartColumn);

            int targetIndex = currentVisIndex + delta;
            if (targetIndex < 0 || targetIndex >= _visualLines.Count)
                return; // nothing above/below

            var targetVis = _visualLines[targetIndex];

            // Target line text (where we want to move the caret)
            string targetLineText = Document.Lines[targetVis.DocLineIndex] ?? string.Empty;
            int lineLen = targetLineText.Length;

            // Clamp visual segment to actual line length for safety
            int segStart = System.Math.Clamp(targetVis.StartColumn, 0, lineLen);
            int segEnd = System.Math.Clamp(segStart + targetVis.Length, 0, lineLen);

            int col = 0;
            int bestIndex = segStart;
            int bestDiff = int.MaxValue;

            // Walk characters on the TARGET line, computing visual columns
            for (int i = segStart; i <= segEnd; i++)
            {
                if (i > segStart)
                {
                    char ch = targetLineText[i - 1];
                    if (ch == '\t')
                    {
                        col = ((col / TabSize) + 1) * TabSize;
                    }
                    else
                    {
                        col++;
                    }
                }

                int diff = System.Math.Abs(col - currentCols);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIndex = i;
                }
            }

            int newCol = System.Math.Clamp(bestIndex, 0, lineLen);
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
            LineSpan range;

            if (TryGetSelectionSpan(out var span))
            {
                range = span;
                text = Document.GetText(span);
            }
            else
            {
                // Cut entire document
                if (Document.Lines.Count == 0)
                    return;

                int lastLine = Document.Lines.Count - 1;
                int lastCol = Document.Lines[lastLine]?.Length ?? 0;
                range = new LineSpan(0, 0, lastLine, lastCol);
                text = Document.GetText();
            }

            await top.Clipboard.SetTextAsync(text);

            var op = ReplaceRangeOperation.CreateAndApply(
                Document,
                this,
                range,
                string.Empty,
                clearSelectionAfter: true);

            PushOperation(op, allowMerge: false);

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

            LineSpan range;
            if (TryGetSelectionSpan(out var span))
            {
                range = span;
            }
            else
            {
                range = new LineSpan(
                    Document.CaretLine,
                    Document.CaretColumn,
                    Document.CaretLine,
                    Document.CaretColumn);
            }

            var op = ReplaceRangeOperation.CreateAndApply(
                Document,
                this,
                range,
                text,
                clearSelectionAfter: true);

            PushOperation(op, allowMerge: false);

            _text = Document.GetText();
            InvalidateVisual();
        }
    }
}
