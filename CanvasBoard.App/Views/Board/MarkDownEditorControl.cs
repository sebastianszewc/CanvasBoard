using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl : Control
    {
        public MarkdownDocument Document { get; } = new();

        private readonly Typeface _baseTypeface =
            new(new FontFamily("Consolas"), FontStyle.Normal, FontWeight.Normal);

        private const double BaseFontSize = 14.0;
        private const double LineSpacing = 1.4;
        private const double LeftPadding = 6.0;
        private const double TableCellPaddingX = 6.0;

        public MarkdownEditorControl()
        {
            Focusable = true;
        }

        // --------- Table structures ---------

        private sealed class TableRow
        {
            public int LineIndex;
            public List<string> Cells = new();
            public bool IsSeparator; // true for |---|---| line
            public int VisualLines = 1; // how many stacked lines this row needs (from <br>)
        }

        private sealed class TableGroup
        {
            public List<TableRow> Rows = new();
            public double[] ColumnWidths = Array.Empty<double>();
        }

        private sealed class TableRowRef
        {
            public TableGroup Group = null!;
            public int RowIndex;
        }

        private readonly List<TableGroup> _tableGroups = new();
        private readonly Dictionary<int, TableRowRef> _tableRowByLine = new();

        // Hit regions for table UI (+ row / + col)
        private enum TableHitType
        {
            AddRow,
            AddColumn
        }

        private sealed class TableHitRegion
        {
            public Rect Rect;
            public TableHitType Type;
            public TableGroup Group = null!;
        }

        private readonly List<TableHitRegion> _tableHitRegions = new();

        // --------- Visual line representation ---------

        private sealed class VisualLine
        {
            public int DocLineIndex;
            public int StartColumn;
            public int Length;

            public bool IsFirstSegmentOfLogicalLine;
            public bool IsInCodeBlock;
            public bool IsFenceLine;
            public int HeadingLevel;
            public bool IsBullet;
            public bool IsQuote;
            public bool IsHorizontalRule;

            public TableRowRef? TableRef;

            // For table rows spanning multiple vertical slots
            public int TableVisualIndex;  // 0..TableVisualCount-1
            public int TableVisualCount;  // how many visual slots this row occupies
        }

        private readonly List<VisualLine> _visualLines = new();

        // --------- Avalonia binding ---------

        public static readonly DirectProperty<MarkdownEditorControl, string> TextProperty =
            AvaloniaProperty.RegisterDirect<MarkdownEditorControl, string>(
                nameof(Text),
                o => o.Text,
                (o, v) => o.Text = v,
                string.Empty);

        private string _text = string.Empty;
        public string Text
        {
            get => _text;
            set
            {
                var val = value ?? string.Empty;
                if (_text == val)
                    return;

                _text = val;
                Document.SetText(_text);
                InvalidateVisual();
            }
        }

        // --------- Measure / Render ---------

        protected override Size MeasureOverride(Size availableSize)
        {
            return availableSize;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            EnsureLayout();

            var lineHeight = BaseFontSize * LineSpacing;

            // Background
            var bg = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
            context.FillRectangle(bg, new Rect(Bounds.Size));

            _tableHitRegions.Clear();

            // Lines
            for (int i = 0; i < _visualLines.Count; i++)
            {
                var vis = _visualLines[i];
                double y = i * lineHeight;

                string rawLine = vis.DocLineIndex >= 0 && vis.DocLineIndex < Document.Lines.Count
                    ? (Document.Lines[vis.DocLineIndex] ?? string.Empty)
                    : string.Empty;

                string segmentText = ExtractSegment(rawLine, vis.StartColumn, vis.Length);

                bool isActiveDocLine = IsFocused && vis.DocLineIndex == Document.CaretLine;
                bool isTableLine = vis.TableRef != null;

                if (isTableLine)
                {
                    // Only the first visual slot of the row actually draws the table;
                    // the extra VisualLines entries just reserve vertical space.
                    if (vis.TableVisualIndex == 0)
                    {
                        if (isActiveDocLine &&
                            TryGetTableCellFromCaret(
                                vis.DocLineIndex,
                                Document.CaretColumn,
                                out var rowRef,
                                out var activeCell,
                                out var cellStart,
                                out var cellEnd))
                        {
                            // Draw table row (background + grid, but no text for active cell)
                            DrawTableRow(context, rowRef, y, activeCell);

                            // Now draw the active cell as multiple stacked lines (without <br>)
                            string line = rawLine;
                            int len = Math.Max(0, Math.Min(cellEnd, line.Length) - cellStart);
                            string cellTextRaw = len > 0 ? line.Substring(cellStart, len) : string.Empty;

                            DrawActiveTableCellEditor(context, rowRef, activeCell, cellTextRaw, y);
                        }
                        else if (vis.TableRef != null)
                        {
                            // Non-active table row: fully rendered
                            DrawTableRow(context, vis.TableRef, y, -1);
                        }
                    }

                    // Skip further processing for table rows
                    continue;
                }

                if (isActiveDocLine)
                {
                    // Active non-table line: raw markdown
                    DrawPlainSegment(context, segmentText, y);
                }
                else
                {
                    // Normal markdown rendering
                    DrawMarkdownSegment(context, vis, rawLine, segmentText, y);
                }
            }

            // Table “+ Row” and “+ Col” UI
            DrawTableUi(context, lineHeight);

            if (IsFocused)
                DrawCaret(context, lineHeight);
        }

        // ================== TEXT INPUT / KEYS ==================

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            if (string.IsNullOrEmpty(e.Text))
                return;

            Document.InsertText(e.Text);
            _text = Document.GetText();
            InvalidateVisual();

            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.Key)
            {
                case Key.Left:
                    Document.MoveCaretLeft();
                    NormalizeCaretAroundBr();
                    break;

                case Key.Right:
                    Document.MoveCaretRight();
                    NormalizeCaretAroundBr();
                    break;

                case Key.Up:
                    Document.MoveCaretUp();
                    SkipSeparatorLines(-1);
                    NormalizeCaretAroundBr();
                    break;

                case Key.Down:
                    Document.MoveCaretDown();
                    SkipSeparatorLines(1);
                    NormalizeCaretAroundBr();
                    break;

                case Key.Home:
                    Document.MoveCaretToLineStart();
                    NormalizeCaretAroundBr();
                    break;

                case Key.End:
                    Document.MoveCaretToLineEnd();
                    NormalizeCaretAroundBr();
                    break;

                case Key.Enter:
                    {
                        // Inside table: Enter = new row below (unless Shift+Enter for <br>)
                        if (TryGetTableCellFromCaret(
                                Document.CaretLine,
                                Document.CaretColumn,
                                out var rowRef,
                                out var cellIndex,
                                out _,
                                out _))
                        {
                            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                            {
                                const string BrToken = "<br>";
                                Document.InsertText(BrToken);
                            }
                            else
                            {
                                AddTableRowBelowCaret(rowRef, cellIndex);
                            }

                            _text = Document.GetText();
                            InvalidateVisual();
                            e.Handled = true;
                            return;
                        }

                        Document.InsertNewLine();
                        _text = Document.GetText();
                        break;
                    }

                case Key.Back:
                    {
                        // Special handling for <br> deletion
                        const string BrToken = "<br>";
                        if (_tableRowByLine.ContainsKey(Document.CaretLine) &&
                            Document.CaretColumn > 0)
                        {
                            string line = Document.Lines[Document.CaretLine] ?? string.Empty;
                            int col = Document.CaretColumn;
                            if (col >= BrToken.Length &&
                                col <= line.Length &&
                                line.Substring(col - BrToken.Length, BrToken.Length) == BrToken)
                            {
                                // Remove whole <br> and move caret before it
                                string newLine = line.Remove(col - BrToken.Length, BrToken.Length);
                                Document.Lines[Document.CaretLine] = newLine;
                                Document.SetCaret(Document.CaretLine, col - BrToken.Length);
                                _text = Document.GetText();
                                InvalidateVisual();
                                e.Handled = true;
                                return;
                            }
                        }

                        // Prevent backspace from deleting table pipes
                        if (IsCaretAtTableCellBoundary(out _, out var cellStart, out _))
                        {
                            if (Document.CaretColumn <= cellStart)
                            {
                                // At left border of cell: do not delete the '|'
                                break;
                            }
                        }

                        Document.Backspace();
                        _text = Document.GetText();
                        break;
                    }

                case Key.Delete:
                    {
                        // Special handling for <br> deletion (forward)
                        const string BrToken = "<br>";
                        if (_tableRowByLine.ContainsKey(Document.CaretLine))
                        {
                            string line = Document.Lines[Document.CaretLine] ?? string.Empty;
                            int col = Document.CaretColumn;
                            if (col + BrToken.Length <= line.Length &&
                                line.Substring(col, BrToken.Length) == BrToken)
                            {
                                // Remove whole <br>, caret stays in same place
                                string newLine = line.Remove(col, BrToken.Length);
                                Document.Lines[Document.CaretLine] = newLine;
                                Document.SetCaret(Document.CaretLine, col);
                                _text = Document.GetText();
                                InvalidateVisual();
                                e.Handled = true;
                                return;
                            }
                        }

                        // Prevent delete from deleting table pipes
                        if (IsCaretAtTableCellBoundary(out var line2, out _, out var cellEnd))
                        {
                            if (Document.CaretColumn >= cellEnd &&
                                Document.CaretColumn < line2.Length &&
                                line2[Document.CaretColumn] == '|')
                            {
                                // At right border of cell: do not delete the '|'
                                break;
                            }
                        }

                        Document.Delete();
                        _text = Document.GetText();
                        break;
                    }

                default:
                    return;
            }

            InvalidateVisual();
            e.Handled = true;
        }

        // ================== MOUSE ==================

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var p = e.GetPosition(this);

            // First, check if clicked on any table UI button
            foreach (var hit in _tableHitRegions)
            {
                if (hit.Rect.Contains(p))
                {
                    HandleTableHit(hit);
                    e.Handled = true;
                    Focus();
                    return;
                }
            }

            EnsureLayout();

            var lineHeight = BaseFontSize * LineSpacing;

            if (_visualLines.Count == 0)
                return;

            int visIndex = (int)(p.Y / lineHeight);
            visIndex = Math.Clamp(visIndex, 0, _visualLines.Count - 1);

            var vis = _visualLines[visIndex];
            string rawLine = vis.DocLineIndex >= 0 && vis.DocLineIndex < Document.Lines.Count
                ? (Document.Lines[vis.DocLineIndex] ?? string.Empty)
                : string.Empty;

            // If this is a table row, choose cell by x and put caret inside that cell
            if (vis.TableRef != null &&
                TryGetTableCellFromX(vis.DocLineIndex, p.X, out var rowRef2,
                    out _, out var cellStart, out var cellEnd))
            {
                int lineEnd = Math.Min(cellEnd, rawLine.Length);
                if (lineEnd < cellStart)
                    lineEnd = cellStart;

                // For multi-line cells (<br>), pick the segment that corresponds to this visual table line
                int segmentStart = cellStart;
                int segmentEnd   = lineEnd;

                int vIndex = vis.TableVisualIndex;
                if (vIndex > 0 && segmentStart < segmentEnd)
                {
                    segmentStart = GetCellVisualSegmentStart(rawLine, cellStart, lineEnd, vIndex);
                    if (segmentStart < cellStart || segmentStart > lineEnd)
                        segmentStart = cellStart;
                }

                int caretCol = GetColumnFromX(rawLine, segmentStart, segmentEnd - segmentStart, p.X);
                caretCol = Math.Clamp(caretCol, segmentStart, segmentEnd);

                Document.SetCaret(vis.DocLineIndex, caretCol);
                NormalizeCaretAroundBr();
            }
            else
            {
                // Normal wrapped line: map x within this visual segment
                int col = GetColumnFromX(rawLine, vis.StartColumn, vis.Length, p.X);
                Document.SetCaret(vis.DocLineIndex, col);
                NormalizeCaretAroundBr();
            }

            _text = Document.GetText();
            InvalidateVisual();

            e.Handled = true;
            Focus();
        }

        // ================== HELPERS (caret / separators) ==================

        private void NormalizeCaretAroundBr()
        {
            if (!_tableRowByLine.ContainsKey(Document.CaretLine))
                return;

            string line = Document.Lines[Document.CaretLine] ?? string.Empty;
            int pos = Document.CaretColumn;
            const string BrToken = "<br>";

            int idx = line.IndexOf(BrToken, StringComparison.Ordinal);
            while (idx >= 0)
            {
                int end = idx + BrToken.Length;

                // If caret is inside the token, snap to the closer edge
                if (pos > idx && pos < end)
                {
                    int toStart = pos - idx;
                    int toEnd = end - pos;
                    int newPos = (toStart <= toEnd) ? idx : end;
                    Document.SetCaret(Document.CaretLine, newPos);
                    return;
                }

                idx = line.IndexOf(BrToken, idx + 1, StringComparison.Ordinal);
            }
        }

        private int GetColumnFromX(string rawLine, int startColumn, int length, double x)
        {
            if (rawLine == null)
                rawLine = string.Empty;

            double localX = x - LeftPadding;
            if (localX <= 0)
                return startColumn;

            int maxCol = Math.Min(rawLine.Length, startColumn + length);
            if (maxCol <= startColumn)
                return startColumn;

            int bestCol = startColumn;
            double bestDiff = double.MaxValue;

            for (int col = startColumn; col <= maxCol; col++)
            {
                string segment = rawLine.Substring(startColumn, col - startColumn);
                double width = MeasureTextWidth(segment, BaseFontSize);
                double diff = Math.Abs(width - localX);

                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestCol = col;
                }
            }

            return bestCol;
        }

        private void SkipSeparatorLines(int direction)
        {
            if (direction == 0)
                return;

            while (true)
            {
                if (!_tableRowByLine.TryGetValue(Document.CaretLine, out var tr) ||
                    !tr.Group.Rows[tr.RowIndex].IsSeparator)
                {
                    break;
                }

                int nextLine = Document.CaretLine + direction;
                if (nextLine < 0 || nextLine >= Document.Lines.Count)
                    break;

                Document.SetCaret(nextLine, Math.Clamp(Document.CaretColumn, 0, Document.Lines[nextLine].Length));
            }
        }

        private int GetCellVisualSegmentStart(string line, int cellStart, int cellEnd, int visualIndex)
        {
            // visualIndex 0 -> first segment (no <br> consumed)
            if (visualIndex <= 0)
                return cellStart;

            const string BrToken = "<br>";

            int start = cellStart;
            int searchPos = cellStart;

            for (int i = 0; i < visualIndex; i++)
            {
                int remaining = cellEnd - searchPos;
                if (remaining <= 0)
                    break;

                int brPos = line.IndexOf(BrToken, searchPos, remaining, StringComparison.Ordinal);
                if (brPos < 0)
                    break;

                start = brPos + BrToken.Length;
                searchPos = start;
            }

            return start;
        }

    }
}
