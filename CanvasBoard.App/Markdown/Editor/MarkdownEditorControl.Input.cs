using System;               
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CanvasBoard.App.Markdown.Document;
using System.Collections.Generic;
using CanvasBoard.App.Markdown.Model;
using CanvasBoard.App.Markdown.Tables;



namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl
    {
        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            if (string.IsNullOrEmpty(e.Text))
                return;

            if (IsCaretInTableStructure())
            {
                // Do not allow typing directly into table structure (pipes, alignment row)
                e.Handled = true;
                return;
            }

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
                    case Key.T:
                        if (shift)
                        {
                            if (ReformatCurrentTable())
                            {
                                e.Handled = true;
                                return;
                            }
                        }
                        break;
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

                    // NEW: inline formatting
                    case Key.B:
                        ToggleInlineMarkup("**");   // bold
                        e.Handled = true;
                        return;

                    case Key.I:
                        ToggleInlineMarkup("_");    // italic
                        e.Handled = true;
                        return;

                    case Key.E:
                        ToggleInlineMarkup("`");    // code span
                        e.Handled = true;
                        return;
                }
            }
            bool handled = false;


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
                    handled = true;   
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
                    handled = true;   
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
                    handled = true;   
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
                    handled = true;   
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
                    handled = true;   
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
                    handled = true;   
                    break;

                case Key.Enter:
                {
                    // If there is a selection, keep behavior simple for now:
                    // just replace selection with a newline (no list/quote magic).
                    if (TryGetSelectionSpan(out var selSpan))
                    {
                        var op = ReplaceRangeOperation.CreateAndApply(
                            Document,
                            this,
                            selSpan,
                            "\n",
                            clearSelectionAfter: true);

                        PushOperation(op, allowMerge: false);
                        _text = Document.GetText();
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                    }

                    HandleSmartEnter();
                    e.Handled = true;
                    return;
                }

                case Key.Back:
                {
                    if (IsCaretInTableStructure())
                    {
                        e.Handled = true;
                        return;
                    }
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
                    handled = true;   
                    break;
                }

                case Key.Delete:
                {
                    if (IsCaretInTableStructure())
                    {
                        e.Handled = true;
                        return;
                    }
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
                    handled = true;
                    break;
                }

                case Key.Tab:
                {
                    // If we’re inside a markdown table, navigate between cells
                    if (TryNavigateTableCell(backwards: shift))
                    {
                        e.Handled = true;
                        return;
                    }

                    // Otherwise: insert a real tab character (or replace selection with one)
                    if (TryGetSelectionSpan(out var selSpan))
                    {
                        var opSel = ReplaceRangeOperation.CreateAndApply(
                            Document,
                            this,
                            selSpan,
                            "\t",
                            clearSelectionAfter: true);

                        PushOperation(opSel, allowMerge: false);
                        _text = Document.GetText();
                    }
                    else
                    {
                        InsertTabCharacter();
                    }

                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
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
    
        // Toggle wrapping of the current selection with a simple inline markdown marker.
        // Examples:
        //   marker="**"  -> **text**
        //   marker="_"   -> _text_
        //   marker="`"   -> `text`
        private void ToggleInlineMarkup(string marker)
        {
            if (string.IsNullOrEmpty(marker))
                return;

            if (!TryGetSelectionSpan(out var span))
                return; // no selection -> do nothing for now

            string selected = Document.GetText(span) ?? string.Empty;

            string newText;

            // Simple unwrap: marker...marker
            if (selected.Length >= marker.Length * 2 &&
                selected.StartsWith(marker, StringComparison.Ordinal) &&
                selected.EndsWith(marker, StringComparison.Ordinal))
            {
                newText = selected.Substring(
                    marker.Length,
                    selected.Length - marker.Length * 2);
            }
            else
            {
                newText = marker + selected + marker;
            }

            var op = ReplaceRangeOperation.CreateAndApply(
                Document,
                this,
                span,
                newText,
                clearSelectionAfter: true);

            PushOperation(op, allowMerge: false);

            _text = Document.GetText();
            InvalidateVisual();
        }
        private void HandleSmartEnter()
        {
            int lineIndex = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            if (lineIndex < 0 || lineIndex >= Document.Lines.Count)
                return;

            string line = Document.Lines[lineIndex] ?? string.Empty;
            int lineLen = line.Length;

            // Basic fallback: if line is empty, just insert a newline
            if (lineLen == 0)
            {
                var spanEmpty = new LineSpan(lineIndex, caretCol, lineIndex, caretCol);
                var opEmpty = ReplaceRangeOperation.CreateAndApply(
                    Document,
                    this,
                    spanEmpty,
                    "\n",
                    clearSelectionAfter: true);

                PushOperation(opEmpty, allowMerge: false);
                _text = Document.GetText();
                InvalidateVisual();
                return;
            }

            // 1) Leading whitespace (indent)
            int indentEnd = 0;
            while (indentEnd < lineLen && (line[indentEnd] == ' ' || line[indentEnd] == '\t'))
                indentEnd++;

            // 2) Optional block quote: one or more '>' with optional spaces, e.g.
            //    "> ", ">> ", "> > ", ">>>"
            int pos = indentEnd;
            bool hasQuote = false;

            while (pos < lineLen && line[pos] == '>')
            {
                hasQuote = true;
                pos++;

                // Allow a single space after each '>' ("> > > text" or ">>> text")
                if (pos < lineLen && line[pos] == ' ')
                    pos++;
            }

            int afterQuotePos = pos;


            // 3) Optional bullet or ordered list marker
            bool hasBullet = false;
            bool hasOrdered = false;

            if (pos < lineLen && (line[pos] == '-' || line[pos] == '*' || line[pos] == '+'))
            {
                hasBullet = true;
                pos++;
                if (pos < lineLen && line[pos] == ' ')
                    pos++;
            }
            else
            {
                int digitsStart = pos;
                while (pos < lineLen && char.IsDigit(line[pos]))
                    pos++;

                if (pos > digitsStart && pos < lineLen && line[pos] == '.')
                {
                    hasOrdered = true;
                    pos++;
                    if (pos < lineLen && line[pos] == ' ')
                        pos++;
                }
                else
                {
                    // No valid ordered list; reset to after quote
                    pos = afterQuotePos;
                }
            }

            int prefixEnd = pos;
            string newLinePrefix = line.Substring(0, prefixEnd);
            string contentAfterPrefix = prefixEnd <= lineLen ? line.Substring(prefixEnd) : string.Empty;

            bool hasStructure = hasQuote || hasBullet || hasOrdered;
            bool onlyWhitespaceAfterPrefix = contentAfterPrefix.Trim().Length == 0;

            // Helper: plain newline (no markdown-aware behavior)
            void PlainNewLine()
            {
                string afterCaret = caretCol <= lineLen ? line.Substring(caretCol) : string.Empty;

                var span = new LineSpan(lineIndex, caretCol, lineIndex, lineLen);
                var op = ReplaceRangeOperation.CreateAndApply(
                    Document,
                    this,
                    span,
                    "\n" + afterCaret,
                    clearSelectionAfter: true);

                PushOperation(op, allowMerge: false);
                _text = Document.GetText();
                InvalidateVisual();
            }

            if (!hasStructure)
            {
                PlainNewLine();
                return;
            }

            // If caret is inside the marker/prefix region, don't try anything clever
            if (caretCol < prefixEnd)
            {
                PlainNewLine();
                return;
            }

            // Exit list/quote: line contains only marker + optional whitespace, caret at end
            if (onlyWhitespaceAfterPrefix && caretCol >= lineLen)
            {
                string indent = line.Substring(0, indentEnd);

                // Replace from indentEnd to end-of-line with "\n" + indent,
                // effectively removing bullet/number/quote and creating a blank indented line.
                var span = new LineSpan(lineIndex, indentEnd, lineIndex, lineLen);
                var op = ReplaceRangeOperation.CreateAndApply(
                    Document,
                    this,
                    span,
                    "\n" + indent,
                    clearSelectionAfter: true);

                PushOperation(op, allowMerge: false);
                _text = Document.GetText();
                InvalidateVisual();
                return;
            }

            // Continue list/quote: replicate prefix on the new line
            {
                string afterCaret = caretCol <= lineLen ? line.Substring(caretCol) : string.Empty;
                string insertText = "\n" + newLinePrefix + afterCaret;

                // Replace from caret to end-of-line with "\n" + prefix + rest-of-line-after-caret
                var span = new LineSpan(lineIndex, caretCol, lineIndex, lineLen);
                var op = ReplaceRangeOperation.CreateAndApply(
                    Document,
                    this,
                    span,
                    insertText,
                    clearSelectionAfter: true);

                PushOperation(op, allowMerge: false);
                _text = Document.GetText();
                InvalidateVisual();
            }
        }
    
        // ----------------------------
        // Table-aware caret navigation
        // ----------------------------

        private struct CellSegment
        {
            public int Start; // inclusive
            public int End;   // exclusive
        }

        private void InsertTabCharacter()
        {
            int lineIndex = Document.CaretLine;
            int col = Document.CaretColumn;

            if (lineIndex < 0 || lineIndex >= Document.Lines.Count)
                return;

            var span = new LineSpan(lineIndex, col, lineIndex, col);
            var op = ReplaceRangeOperation.CreateAndApply(
                Document,
                this,
                span,
                "\t",
                clearSelectionAfter: true);

            PushOperation(op, allowMerge: true);
            _text = Document.GetText();
        }

        // ----------------------------
        // Table-aware caret navigation
        // ----------------------------

        private bool TryNavigateTableCell(bool backwards)
        {
            if (!TryGetCurrentTableCell(out TableBlock tableBlock, out int rowIndex, out int colIndex))
                return false;

            var table = tableBlock.Table;

            int lastRow = table.RowCount - 1;
            int lastCol = table.ColumnCount - 1;

            int newRow = rowIndex;
            int newCol = colIndex;

            if (!backwards)
            {
                // Tab forward
                if (colIndex < lastCol)
                {
                    newCol = colIndex + 1;
                }
                else if (rowIndex < lastRow)
                {
                    newRow = rowIndex + 1;
                    newCol = 0;
                }
                else
                {
                    // At last cell of last row: fall back to normal tab behavior
                    return false;
                }
            }
            else
            {
                // Shift+Tab backwards
                if (colIndex > 0)
                {
                    newCol = colIndex - 1;
                }
                else if (rowIndex > 0)
                {
                    newRow = rowIndex - 1;
                    newCol = lastCol;
                }
                else
                {
                    // At first cell of first row
                    return false;
                }
            }

            MoveCaretToTableCell(tableBlock, newRow, newCol);
            return true;
        }

        private bool TryGetCurrentTableCell(
            out TableBlock tableBlock,
            out int rowIndex,
            out int colIndex)
        {
            tableBlock = null!;
            rowIndex = -1;
            colIndex = -1;

            // Ensure model is up to date
            EnsureModel();

            int lineIndex = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            if (lineIndex < 0 || lineIndex >= Document.Lines.Count)
                return false;

            int offset = Document.GetOffset(lineIndex, caretCol);

            var block = FindTableBlockAtOffset(offset);
            if (block == null)
                return false;

            // Find cell whose span contains this offset
            int rows = block.RowCount;
            int cols = block.ColumnCount;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var span = block.CellSpans[r, c];
                    if (span.Contains(offset))
                    {
                        tableBlock = block;
                        rowIndex = r;
                        colIndex = c;
                        return true;
                    }
                }
            }

            // Fallback: if not inside any cell span, find nearest cell by span center
            int bestR = 0;
            int bestC = 0;
            int bestDist = int.MaxValue;
            bool found = false;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var span = block.CellSpans[r, c];
                    int center = span.Start + span.Length / 2;
                    int dist = Math.Abs(center - offset);

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestR = r;
                        bestC = c;
                        found = true;
                    }
                }
            }

            if (!found)
                return false;

            tableBlock = block;
            rowIndex = bestR;
            colIndex = bestC;
            return true;
        }

        private void MoveCaretToTableCell(
            TableBlock tableBlock,
            int rowIndex,
            int colIndex)
        {
            var table = tableBlock.Table;

            if (table.ColumnCount <= 0 || table.RowCount <= 0)
                return;

            rowIndex = Math.Clamp(rowIndex, 0, table.RowCount - 1);
            colIndex = Math.Clamp(colIndex, 0, table.ColumnCount - 1);

            var span = tableBlock.CellSpans[rowIndex, colIndex];

            // Move caret to start of cell
            int targetOffset = span.Start;

            var (line, column) = Document.GetLineColumn(targetOffset);

            Document.SetCaretPosition(line, column);

            ClearSelection();
            InvalidateVisual();
        }

        private static List<CellSegment> ComputeTableLineSegments(string line)
        {
            var segments = new List<CellSegment>();

            if (string.IsNullOrEmpty(line))
                return segments;

            int len = line.Length;

            // Collect positions of all '|' characters
            var bars = new List<int>();
            for (int i = 0; i < len; i++)
            {
                if (line[i] == '|')
                    bars.Add(i);
            }

            if (bars.Count == 0)
            {
                // No pipes, treat whole line as a single segment
                segments.Add(new CellSegment { Start = 0, End = len });
                return segments;
            }

            // Consider whether there is a leading pipe after optional whitespace
            int firstNonWs = 0;
            while (firstNonWs < len && (line[firstNonWs] == ' ' || line[firstNonWs] == '\t'))
                firstNonWs++;

            bool hasLeadingPipe = firstNonWs < len && line[firstNonWs] == '|';

            if (hasLeadingPipe)
            {
                // Example: "| a | b |", or "  | a | b |"
                for (int i = 0; i < bars.Count - 1; i++)
                {
                    int start = bars[i] + 1;
                    int end = bars[i + 1];

                    if (end > start)
                        segments.Add(new CellSegment { Start = start, End = end });
                }

                // Trailing segment if there is content after the last '|'
                int lastBar = bars[bars.Count - 1];
                if (lastBar < len - 1)
                {
                    segments.Add(new CellSegment
                    {
                        Start = lastBar + 1,
                        End = len
                    });
                }
            }
            else
            {
                // Example: "a | b | c" (no leading pipe)
                // First segment: from start to first pipe
                segments.Add(new CellSegment
                {
                    Start = 0,
                    End = bars[0]
                });

                // Middle segments: between pipes
                for (int i = 0; i < bars.Count - 1; i++)
                {
                    int start = bars[i] + 1;
                    int end = bars[i + 1];

                    if (end > start)
                        segments.Add(new CellSegment { Start = start, End = end });
                }

                // Trailing segment if there is content after last pipe
                int lastBar = bars[bars.Count - 1];
                if (lastBar < len - 1)
                {
                    segments.Add(new CellSegment
                    {
                        Start = lastBar + 1,
                        End = len
                    });
                }
            }
            return segments;
        }

        // ----------------------------
        // Table reformatting (Ctrl+Shift+T)
        // ----------------------------

        private bool ReformatCurrentTable()
        {
            // Ensure model is up to date
            EnsureModel();

            int lineIndex = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            if (lineIndex < 0 || lineIndex >= Document.Lines.Count)
                return false;

            int offset = Document.GetOffset(lineIndex, caretCol);

            var tableBlock = FindTableBlockAtOffset(offset);
            if (tableBlock == null)
                return false;

            var table = tableBlock.Table;

            if (table.ColumnCount <= 0 || table.RowCount <= 0)
                return false;

            string newTableText = BuildReformattedTableText(table);

            // Replace from start of header line to end of last row line
            int startLine = tableBlock.StartLine;
            int endLine = tableBlock.EndLine;

            string lastLineText = Document.Lines[endLine] ?? string.Empty;

            var span = new LineSpan(
                startLine,
                0,
                endLine,
                lastLineText.Length);

            var op = ReplaceRangeOperation.CreateAndApply(
                Document,
                this,
                span,
                newTableText,
                clearSelectionAfter: true);

            PushOperation(op, allowMerge: false);
            _text = Document.GetText();
            InvalidateVisual();

            return true;
        }

        private static string BuildReformattedTableText(TableModel table)
        {
            int rows = table.RowCount;
            int cols = table.ColumnCount;

            var colWidths = new int[cols];

            // Determine column widths from cell contents
            for (int c = 0; c < cols; c++)
            {
                int max = 3; // minimum
                for (int r = 0; r < rows; r++)
                {
                    string cell = table.GetCell(r, c) ?? string.Empty;
                    if (cell.Length > max)
                        max = cell.Length;
                }

                colWidths[c] = max;
            }

            var lines = new List<string>();

            // Header row (row 0)
            {
                var cells = new List<string>(cols);
                for (int c = 0; c < cols; c++)
                {
                    string cellText = table.GetCell(0, c) ?? string.Empty;
                    string padded = PadCell(cellText, colWidths[c], table.Alignments[c]);
                    cells.Add(padded);
                }

                string headerLine = "| " + string.Join(" | ", cells) + " |";
                lines.Add(headerLine);
            }

            // Alignment/separator row
            {
                var pieces = new List<string>(cols);
                for (int c = 0; c < cols; c++)
                {
                    var align = table.Alignments[c];
                    int innerWidth = Math.Max(3, colWidths[c] - 2);

                    string dashes = new string('-', innerWidth);
                    string cell = align switch
                    {
                        TableAlignment.Left   => ":" + dashes + " ",
                        TableAlignment.Right  => " " + dashes + ":",
                        TableAlignment.Center => ":" + dashes + ":",
                        _                     => " " + dashes + " "
                    };
                    pieces.Add(cell);
                }

                string alignLine = "| " + string.Join(" | ", pieces) + " |";
                lines.Add(alignLine);
            }

            // Body rows: rows 1..rows-1
            for (int r = 1; r < rows; r++)
            {
                var cells = new List<string>(cols);
                for (int c = 0; c < cols; c++)
                {
                    string cellText = table.GetCell(r, c) ?? string.Empty;
                    string padded = PadCell(cellText, colWidths[c], table.Alignments[c]);
                    cells.Add(padded);
                }

                string rowLine = "| " + string.Join(" | ", cells) + " |";
                lines.Add(rowLine);
            }

            return string.Join("\n", lines);
        }

        private static string PadCell(string text, int width, TableAlignment align)
        {
            text ??= string.Empty;

            if (width < text.Length)
                width = text.Length;

            int padding = width - text.Length;

            return align switch
            {
                TableAlignment.Right =>
                    new string(' ', padding) + text,

                TableAlignment.Center =>
                    new string(' ', padding / 2) + text + new string(' ', padding - padding / 2),

                _ => // Left or Default
                    text + new string(' ', padding),
            };
        }

        private bool IsCaretInTableStructure()
        {
            // Ensure table engine is fresh

            int lineIndex = Document.CaretLine;
            if (lineIndex < 0 || lineIndex >= Document.Lines.Count)
                return false;

            var table = FindTableForLine(lineIndex);
            if (table == null)
                return false;

            // If caret is inside a regular cell, it's NOT "structure"
            if (TryGetCurrentTableCell(out _, out _, out _))
                return false;

            // We're in a table, but not in a cell -> pipes or alignment row
            return true;
        }

        private TableModel? FindTableForLine(int lineIndex)
        {
            // Make sure _model is parsed
            EnsureModel();
            if (_model == null)
                return null;

            if (lineIndex < 0 || lineIndex >= Document.Lines.Count)
                return null;

            foreach (var block in _model.Blocks)
            {
                if (block.Kind != MarkdownBlockKind.Table)
                    continue;

                if (lineIndex >= block.StartLine && lineIndex <= block.EndLine)
                {
                    var tb = (TableBlock)block;
                    return tb.Table;
                }
            }

            return null;
        }
    }
}
