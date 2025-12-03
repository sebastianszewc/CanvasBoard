using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CanvasBoard.App.Views.Board
{
    public class MarkdownEditorControl : Control
    {
        public MarkdownDocument Document { get; } = new();

        private readonly Typeface _baseTypeface =
            new(new FontFamily("Consolas"), FontStyle.Normal, FontWeight.Normal);

        private const double BaseFontSize = 14.0;
        private const double LineSpacing = 1.4;
        private const double LeftPadding = 6.0;
        private const double TableCellPaddingX = 6.0;

        // --------- Table block structures ---------

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
            public TableGroup Group;
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
            public TableGroup Group;
            public TableHitType Type;
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

            public TableRowRef TableRef; // null if not table row

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

        public MarkdownEditorControl()
        {
            Focusable = true;
        }

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
                        else
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

        // ================== LAYOUT ==================

        private void EnsureLayout()
        {
            _visualLines.Clear();

            BuildTableGroups();

            double availableWidth = Bounds.Width;
            if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
                availableWidth = 1000;

            double textWidth = Math.Max(0, availableWidth - LeftPadding * 2);

            bool inCodeFence = false;

            for (int li = 0; li < Document.Lines.Count; li++)
            {
                string rawLine = Document.Lines[li] ?? string.Empty;
                string trimmedStart = rawLine.TrimStart();
                string trimmed = rawLine.Trim();

                bool isFenceLine = false;
                if (trimmedStart.StartsWith("```"))
                {
                    isFenceLine = true;
                    inCodeFence = !inCodeFence;
                }

                bool isInCodeBlock = inCodeFence && !isFenceLine;

                _tableRowByLine.TryGetValue(li, out var tableRef);
                bool isTableLine = tableRef != null;
                bool isTableSeparator = isTableLine && tableRef.Group.Rows[tableRef.RowIndex].IsSeparator;

                int headingLevel = 0;
                bool isBullet = false;
                bool isQuote = false;
                bool isHorizontalRule = false;

                if (!isInCodeBlock)
                {
                    // Heading (#...)
                    {
                        int idx = 0;
                        while (idx < rawLine.Length && char.IsWhiteSpace(rawLine[idx]))
                            idx++;

                        int sharpCount = 0;
                        int hIdx = idx;
                        while (hIdx < rawLine.Length && rawLine[hIdx] == '#')
                        {
                            sharpCount++;
                            hIdx++;
                        }

                        if (sharpCount > 0 && hIdx < rawLine.Length && rawLine[hIdx] == ' ')
                        {
                            headingLevel = Math.Clamp(sharpCount, 1, 6);
                        }
                    }

                    // Bullet (- / *)
                    if (headingLevel == 0)
                    {
                        var t = trimmedStart;
                        if (t.StartsWith("- ") || t.StartsWith("* "))
                            isBullet = true;
                    }

                    // Blockquote
                    {
                        var t = trimmedStart;
                        if (t.StartsWith(">"))
                            isQuote = true;
                    }

                    // Horizontal rule (not a table)
                    if (!isTableLine && trimmed.Length >= 3)
                    {
                        bool allHrChars = true;
                        foreach (var ch in trimmed)
                        {
                            if (ch != '-' && ch != '*' && ch != '_' && !char.IsWhiteSpace(ch))
                            {
                                allHrChars = false;
                                break;
                            }
                        }
                        if (allHrChars)
                            isHorizontalRule = true;
                    }
                }

                // Tables: non-separator rows become multiple visual lines based on VisualLines
                if (isTableLine && !isInCodeBlock && !isFenceLine)
                {
                    if (!isTableSeparator)
                    {
                        var row = tableRef.Group.Rows[tableRef.RowIndex];
                        int vCount = Math.Max(1, row.VisualLines);

                        for (int v = 0; v < vCount; v++)
                        {
                            _visualLines.Add(new VisualLine
                            {
                                DocLineIndex = li,
                                StartColumn = 0,
                                Length = rawLine.Length,
                                IsFirstSegmentOfLogicalLine = (v == 0),
                                IsInCodeBlock = false,
                                IsFenceLine = false,
                                HeadingLevel = headingLevel,
                                IsBullet = false,
                                IsQuote = isQuote,
                                IsHorizontalRule = false,
                                TableRef = tableRef,
                                TableVisualIndex = v,
                                TableVisualCount = vCount
                            });
                        }
                    }
                    continue;
                }

                // Empty line
                if (rawLine.Length == 0)
                {
                    _visualLines.Add(new VisualLine
                    {
                        DocLineIndex = li,
                        StartColumn = 0,
                        Length = 0,
                        IsFirstSegmentOfLogicalLine = true,
                        IsInCodeBlock = isInCodeBlock,
                        IsFenceLine = isFenceLine,
                        HeadingLevel = headingLevel,
                        IsBullet = isBullet,
                        IsQuote = isQuote,
                        IsHorizontalRule = isHorizontalRule,
                        TableRef = null
                    });
                    continue;
                }

                // Horizontal rule
                if (isHorizontalRule && !isInCodeBlock && !isFenceLine)
                {
                    _visualLines.Add(new VisualLine
                    {
                        DocLineIndex = li,
                        StartColumn = 0,
                        Length = rawLine.Length,
                        IsFirstSegmentOfLogicalLine = true,
                        IsInCodeBlock = false,
                        IsFenceLine = false,
                        HeadingLevel = headingLevel,
                        IsBullet = false,
                        IsQuote = isQuote,
                        IsHorizontalRule = true,
                        TableRef = null
                    });
                    continue;
                }

                // Normal / heading / quote / code lines: wrap
                int start = 0;
                bool firstSeg = true;
                int remaining = rawLine.Length;

                while (remaining > 0)
                {
                    int len = FindMaxFittingLengthWordAware(rawLine, start, remaining, textWidth);
                    if (len <= 0)
                        len = remaining;

                    _visualLines.Add(new VisualLine
                    {
                        DocLineIndex = li,
                        StartColumn = start,
                        Length = len,
                        IsFirstSegmentOfLogicalLine = firstSeg,
                        IsInCodeBlock = isInCodeBlock,
                        IsFenceLine = isFenceLine,
                        HeadingLevel = headingLevel,
                        IsBullet = isBullet,
                        IsQuote = isQuote,
                        IsHorizontalRule = isHorizontalRule,
                        TableRef = null,
                        TableVisualIndex = 0,
                        TableVisualCount = 1
                    });

                    firstSeg = false;
                    start += len;
                    remaining -= len;
                }
            }

            if (_visualLines.Count == 0)
            {
                _visualLines.Add(new VisualLine
                {
                    DocLineIndex = 0,
                    StartColumn = 0,
                    Length = 0,
                    IsFirstSegmentOfLogicalLine = true
                });
            }
        }

        // Build table blocks and shared column widths
        private void BuildTableGroups()
        {
            _tableGroups.Clear();
            _tableRowByLine.Clear();

            bool inCodeFence = false;
            TableGroup current = null;

            for (int li = 0; li < Document.Lines.Count; li++)
            {
                string rawLine = Document.Lines[li] ?? string.Empty;
                string trimmedStart = rawLine.TrimStart();

                bool isFenceLine = false;
                if (trimmedStart.StartsWith("```"))
                {
                    isFenceLine = true;
                    inCodeFence = !inCodeFence;
                }

                // Never treat lines inside ``` blocks as tables
                if (inCodeFence || isFenceLine)
                {
                    if (current != null)
                    {
                        FinalizeTableGroup(current);
                        current = null;
                    }
                    continue;
                }

                var parsedRow = ParseTableRow(rawLine, li);

                if (parsedRow != null)
                {
                    // This line is part of a table
                    if (current == null)
                    {
                        current = new TableGroup();
                        _tableGroups.Add(current);
                    }

                    current.Rows.Add(parsedRow);
                }
                else
                {
                    // End any current table block
                    if (current != null)
                    {
                        FinalizeTableGroup(current);
                        current = null;
                    }
                }
            }

            if (current != null)
                FinalizeTableGroup(current);

            // Build line → table row lookup
            foreach (var group in _tableGroups)
            {
                for (int ri = 0; ri < group.Rows.Count; ri++)
                {
                    var row = group.Rows[ri];
                    _tableRowByLine[row.LineIndex] = new TableRowRef
                    {
                        Group = group,
                        RowIndex = ri
                    };
                }
            }
        }

        private static TableRow ParseTableRow(string rawLine, int lineIndex)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return null;

            var trimmed = rawLine.Trim();

            // Must look like a canonical pipe row: start and end with '|'
            if (!trimmed.StartsWith("|") || !trimmed.EndsWith("|"))
                return null;

            // Must have at least 2 pipes
            int pipeCount = 0;
            foreach (var ch in trimmed)
                if (ch == '|') pipeCount++;
            if (pipeCount < 2)
                return null;

            // Split into interior cells:
            // "| A | B |" -> ["", " A ", " B ", ""]
            var parts = trimmed.Split('|');
            if (parts.Length < 3)
                return null;

            var cells = new List<string>();
            for (int i = 1; i < parts.Length - 1; i++)
            {
                string t = parts[i].Trim();
                cells.Add(t);
            }

            if (cells.Count == 0)
                return null;

            // Separator detection: non-empty cells must be only '-' and ':' and include a '-'
            bool anyNonEmpty = false;
            bool allSeparatorLike = true;

            foreach (var c in cells)
            {
                var t = c.Trim();
                if (t.Length == 0)
                    continue;

                anyNonEmpty = true;

                bool hasDash = false;
                bool onlyDashOrColon = true;

                foreach (var ch in t)
                {
                    if (ch == '-')
                    {
                        hasDash = true;
                    }
                    else if (ch != ':')
                    {
                        onlyDashOrColon = false;
                        break;
                    }
                }

                if (!(onlyDashOrColon && hasDash))
                {
                    allSeparatorLike = false;
                    break;
                }
            }

            bool isSeparator = anyNonEmpty && allSeparatorLike;

            return new TableRow
            {
                LineIndex = lineIndex,
                Cells = cells,
                IsSeparator = isSeparator,
                VisualLines = 1
            };
        }

        private static readonly string[] BrSeparators = new[] { "<br>", "<br/>", "<br />" };

        private void FinalizeTableGroup(TableGroup group)
        {
            int colCount = 0;
            foreach (var row in group.Rows)
                if (row.Cells.Count > colCount)
                    colCount = row.Cells.Count;

            if (colCount == 0)
            {
                group.ColumnWidths = Array.Empty<double>();
                return;
            }

            group.ColumnWidths = new double[colCount];

            double fontSize = BaseFontSize;

            // Widths from NON-separator rows only
            foreach (var row in group.Rows)
            {
                if (row.IsSeparator)
                    continue;

                for (int c = 0; c < row.Cells.Count; c++)
                {
                    string text = row.Cells[c];

                    // Measure the widest *sub-line* split by <br>, not the raw text
                    double maxCellWidth = 0;
                    var parts = text.Split(BrSeparators, StringSplitOptions.None);
                    if (parts.Length == 0)
                        parts = new[] { string.Empty };

                    foreach (var part in parts)
                    {
                        double ww = MeasureTextWidth(part, fontSize);
                        if (ww > maxCellWidth)
                            maxCellWidth = ww;
                    }

                    double w = maxCellWidth + TableCellPaddingX * 2;
                    if (w > group.ColumnWidths[c])
                        group.ColumnWidths[c] = w;
                }
            }

            for (int c = 0; c < colCount; c++)
            {
                if (group.ColumnWidths[c] < 20)
                    group.ColumnWidths[c] = 20;
            }

            // Compute how many visual lines each row needs based on <br>
            foreach (var row in group.Rows)
            {
                if (row.IsSeparator)
                {
                    row.VisualLines = 1;
                    continue;
                }

                int maxLines = 1;
                foreach (var cell in row.Cells)
                {
                    var parts = cell.Split(BrSeparators, StringSplitOptions.None);
                    if (parts.Length > maxLines)
                        maxLines = parts.Length;
                }

                row.VisualLines = Math.Max(1, maxLines);
            }
        }

        private string ExtractSegment(string raw, int start, int length)
        {
            if (start >= raw.Length)
                return string.Empty;

            int len = Math.Min(length, raw.Length - start);
            if (len <= 0)
                return string.Empty;

            return raw.Substring(start, len);
        }

        private int FindMaxFittingLengthWordAware(string line, int start, int maxLen, double textWidth)
        {
            if (textWidth <= 0)
                return maxLen;

            int low = 1;
            int high = maxLen;
            int best = 1;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                string part = line.Substring(start, mid);
                double w = MeasureTextWidth(part, BaseFontSize);

                if (w <= textWidth)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            string seg = line.Substring(start, best);
            int lastBreak = seg.LastIndexOfAny(new[] { ' ', '\t' });
            if (lastBreak > 0 && best < maxLen)
                best = lastBreak + 1;

            return best;
        }

        // ================== MEASUREMENT ==================

        private double MeasureTextWidth(string text, double fontSize)
        {
            const string Probe = "·";

            var full = new FormattedText(
                text + Probe,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _baseTypeface,
                fontSize,
                Brushes.White);

            var probeOnly = new FormattedText(
                Probe,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _baseTypeface,
                fontSize,
                Brushes.White);

            return Math.Max(0, full.Width - probeOnly.Width);
        }

        // ================== BASIC PLAIN RENDER ==================

        private void DrawPlainSegment(DrawingContext context, string segmentText, double y)
        {
            var brush = Brushes.White;

            var ft = new FormattedText(
                segmentText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _baseTypeface,
                BaseFontSize,
                brush);

            context.DrawText(ft, new Point(LeftPadding, y));
        }

        private sealed class RunSegment
        {
            public string Text = string.Empty;
            public bool Bold;
            public bool Italic;
            public bool Code;
        }

        // ================== MARKDOWN RENDER ==================

        private void DrawMarkdownSegment(
            DrawingContext context,
            VisualLine vis,
            string rawLine,
            string segmentText,
            double y)
        {
            bool isHeading = vis.HeadingLevel > 0;
            bool isBullet = vis.IsBullet;
            bool isQuote = vis.IsQuote;
            bool isFenceLine = vis.IsFenceLine;
            bool isCodeBlock = vis.IsInCodeBlock;
            bool isHr = vis.IsHorizontalRule;
            bool isTableLine = vis.TableRef != null;

            // ``` fence lines: hidden in preview
            if (isFenceLine)
                return;

            // Horizontal rule
            if (isHr)
            {
                DrawHorizontalRule(context, y);
                return;
            }

            // Table row (non-active): handled in Render via DrawTableRow
            if (isTableLine)
            {
                return;
            }

            double x = LeftPadding;
            double fontSize = BaseFontSize;

            // Blockquote: leading bar + strip '>'
            int quoteMarkerSkip = 0;
            if (isQuote && vis.IsFirstSegmentOfLogicalLine)
            {
                int idx = 0;
                while (idx < rawLine.Length && char.IsWhiteSpace(rawLine[idx]))
                    idx++;

                if (idx < rawLine.Length && rawLine[idx] == '>')
                {
                    quoteMarkerSkip = idx + 1;
                    if (quoteMarkerSkip < rawLine.Length && rawLine[quoteMarkerSkip] == ' ')
                        quoteMarkerSkip++;
                }

                var barBrush = new SolidColorBrush(Color.FromArgb(160, 120, 120, 255));
                var barRect = new Rect(LeftPadding, y + 2, 3, BaseFontSize * 1.1);
                context.FillRectangle(barBrush, barRect);
                x += 8;
            }

            if (quoteMarkerSkip > 0)
            {
                int localSkip = Math.Max(0, quoteMarkerSkip - vis.StartColumn);
                if (localSkip > 0)
                {
                    if (localSkip >= segmentText.Length)
                        segmentText = string.Empty;
                    else
                        segmentText = segmentText.Substring(localSkip);
                }
            }

            // Headings: strip "#... " in preview
            if (isHeading && vis.IsFirstSegmentOfLogicalLine)
            {
                int idx = 0;
                while (idx < rawLine.Length && char.IsWhiteSpace(rawLine[idx]))
                    idx++;

                while (idx < rawLine.Length && rawLine[idx] == '#')
                    idx++;

                if (idx < rawLine.Length && rawLine[idx] == ' ')
                    idx++;

                int headingSkip = idx;
                if (headingSkip > 0)
                {
                    int localSkip = Math.Max(0, headingSkip - vis.StartColumn);
                    if (localSkip > 0)
                    {
                        if (localSkip >= segmentText.Length)
                            segmentText = string.Empty;
                        else
                            segmentText = segmentText.Substring(localSkip);
                    }
                }
            }

            if (isHeading)
                fontSize = GetHeadingFontSize(vis.HeadingLevel);

            // Bullets: dot + strip "- " / "* "
            if (isBullet && vis.IsFirstSegmentOfLogicalLine)
            {
                double bulletRadius = fontSize * 0.12;
                double centerY = y + fontSize * 0.7;

                var bulletBrush = Brushes.White;
                context.DrawEllipse(
                    bulletBrush,
                    null,
                    new Point(x + bulletRadius, centerY),
                    bulletRadius,
                    bulletRadius);

                x += fontSize * 1.2;

                int idx = 0;
                while (idx < rawLine.Length && char.IsWhiteSpace(rawLine[idx]))
                    idx++;

                int bulletSkip = 0;
                if (idx + 1 < rawLine.Length &&
                    (rawLine[idx] == '-' || rawLine[idx] == '*') &&
                    rawLine[idx + 1] == ' ')
                {
                    bulletSkip = idx + 2;
                }

                if (bulletSkip > 0)
                {
                    int localSkip = Math.Max(0, bulletSkip - vis.StartColumn);
                    if (localSkip > 0)
                    {
                        if (localSkip >= segmentText.Length)
                            segmentText = string.Empty;
                        else
                            segmentText = segmentText.Substring(localSkip);
                    }
                }
            }

            // Code block lines
            if (isCodeBlock)
            {
                var monoTypeface = _baseTypeface;
                var brush = Brushes.White;

                var ft = new FormattedText(
                    segmentText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    monoTypeface,
                    fontSize,
                    brush);

                var bg = new SolidColorBrush(Color.FromArgb(80, 20, 20, 20));
                var rect = new Rect(x, y + fontSize * 0.2, ft.Width + 6, ft.Height * 0.9);
                context.FillRectangle(bg, rect);
                context.DrawText(ft, new Point(x + 3, y));
                return;
            }

            // Inline markdown
            var segments = ParseInlineMarkdown(segmentText);

            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg.Text))
                    continue;

                bool bold = seg.Bold || isHeading;
                bool italic = seg.Italic;
                bool code = seg.Code;

                var typeface = GetTypeface(bold, italic);
                var brush = Brushes.White;

                if (code)
                {
                    var ftCode = new FormattedText(
                        seg.Text,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        brush);

                    var rect = new Rect(x, y + fontSize * 0.25, ftCode.Width + 4, ftCode.Height * 0.8);
                    var bg = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
                    var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);

                    context.FillRectangle(bg, rect);
                    context.DrawRectangle(borderPen, rect);

                    context.DrawText(ftCode, new Point(x + 2, y));
                    x += ftCode.Width + 4;
                }
                else
                {
                    var ft = new FormattedText(
                        seg.Text,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        brush);

                    context.DrawText(ft, new Point(x, y));
                    x += ft.Width;
                }
            }
        }

        // --------- Horizontal rule ---------

        private void DrawHorizontalRule(DrawingContext context, double y)
        {
            double width = Bounds.Width;
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
                width = 200;

            double x1 = LeftPadding;
            double x2 = Math.Max(LeftPadding + 20, width - LeftPadding);

            double yMid = y + BaseFontSize * 0.7;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 180, 180, 180)), 1.0);

            context.DrawLine(pen, new Point(x1, yMid), new Point(x2, yMid));
        }

        // --------- Table rendering ---------

        private void DrawTableRow(DrawingContext context, TableRowRef rowRef, double y, int activeCellIndex = -1)
        {
            var group = rowRef.Group;
            var row = group.Rows[rowRef.RowIndex];

            var cells = row.Cells;
            double[] colWidths = group.ColumnWidths;

            double fontSize = BaseFontSize;
            var typeface = _baseTypeface;
            var brush = Brushes.White;

            // how many vertical slots this row needs (from <br>)
            int visualLines = row.IsSeparator ? 1 : Math.Max(row.VisualLines, 1);
            double rowHeight = BaseFontSize * LineSpacing * visualLines;

            var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 120, 120, 120)), 1);
            var bg = new SolidColorBrush(Color.FromArgb(40, 80, 80, 80));

            // Pre-split cells by <br> to simulate multiline cells
            var cellLinesList = new List<string[]>(colWidths.Length);
            int maxLines = 1;

            for (int c = 0; c < colWidths.Length; c++)
            {
                string cellText = c < cells.Count ? cells[c] : string.Empty;
                var parts = cellText.Split(BrSeparators, StringSplitOptions.None);
                if (parts.Length == 0)
                    parts = new[] { string.Empty };

                cellLinesList.Add(parts);
                if (parts.Length > maxLines)
                    maxLines = parts.Length;
            }

            // Each logical line in the row gets equal vertical share
            double lineHeightInCell = row.IsSeparator
                ? rowHeight
                : rowHeight / Math.Max(1, maxLines);

            double x = LeftPadding;

            for (int c = 0; c < colWidths.Length; c++)
            {
                var cellLines = cellLinesList[c];
                double cellWidth = colWidths[c];

                var rect = new Rect(x, y, cellWidth, rowHeight);
                context.FillRectangle(bg, rect);
                context.DrawRectangle(borderPen, rect);

                // Skip text for the active cell; it will be drawn as editor separately
                if (c != activeCellIndex)
                {
                    using (context.PushClip(new RoundedRect(rect)))
                    {
                        for (int li = 0; li < cellLines.Length; li++)
                        {
                            string lineText = cellLines[li];
                            var ft = new FormattedText(
                                lineText,
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                fontSize,
                                brush);

                            double textY = y + li * lineHeightInCell + (lineHeightInCell - ft.Height) / 2.0;
                            var textPos = new Point(x + TableCellPaddingX, textY);
                            context.DrawText(ft, textPos);
                        }
                    }
                }

                x += cellWidth;
            }
        }

        private void DrawActiveTableCellEditor(
            DrawingContext context,
            TableRowRef rowRef,
            int cellIndex,
            string cellTextRaw,
            double yTop)
        {
            var group = rowRef.Group;
            var row = group.Rows[rowRef.RowIndex];

            var parts = cellTextRaw.Split(BrSeparators, StringSplitOptions.None);
            if (parts.Length == 0)
                parts = new[] { string.Empty };

            int visualLines = Math.Max(row.VisualLines, 1);
            double rowHeight = BaseFontSize * LineSpacing * visualLines;

            int maxLines = Math.Max(visualLines, parts.Length);
            double lineHeightInCell = rowHeight / Math.Max(1, maxLines);

            double cellWidth = group.ColumnWidths[cellIndex];

            double xCellLeft = LeftPadding;
            for (int i = 0; i < cellIndex; i++)
                xCellLeft += group.ColumnWidths[i];

            double textLeft = xCellLeft + TableCellPaddingX;
            var rect = new Rect(xCellLeft, yTop, cellWidth, rowHeight);

            using (context.PushClip(new RoundedRect(rect)))
            {
                for (int li = 0; li < parts.Length; li++)
                {
                    string display = parts[li]; // no <br> here
                    var ft = new FormattedText(
                        display,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        _baseTypeface,
                        BaseFontSize,
                        Brushes.White);

                    double textY = yTop + li * lineHeightInCell + (lineHeightInCell - ft.Height) / 2.0;
                    context.DrawText(ft, new Point(textLeft, textY));
                }
            }
        }

        // Table UI: + Row under table, + Col to the right
        private void DrawTableUi(DrawingContext context, double lineHeight)
        {
            foreach (var group in _tableGroups)
            {
                int minVisIndex = int.MaxValue;
                int maxVisIndex = -1;

                for (int i = 0; i < _visualLines.Count; i++)
                {
                    var vis = _visualLines[i];
                    if (vis.TableRef != null && ReferenceEquals(vis.TableRef.Group, group))
                    {
                        if (i < minVisIndex) minVisIndex = i;
                        if (i > maxVisIndex) maxVisIndex = i;
                    }
                }

                if (maxVisIndex < 0)
                    continue;

                double yTop = minVisIndex * lineHeight;
                double yBottom = (maxVisIndex + 1) * lineHeight;

                double xLeft = LeftPadding;
                double tableWidth = 0;
                foreach (var w in group.ColumnWidths)
                    tableWidth += w;
                double xRight = xLeft + tableWidth;

                // + Row (under last row)
                double rowBtnHeight = lineHeight * 0.7;
                var rowRect = new Rect(xLeft, yBottom + 2, tableWidth, rowBtnHeight);
                var rowBg = new SolidColorBrush(Color.FromArgb(60, 80, 160, 80));
                var rowBorder = new Pen(new SolidColorBrush(Color.FromArgb(120, 120, 200, 120)), 1);
                context.FillRectangle(rowBg, rowRect);
                context.DrawRectangle(rowBorder, rowRect);
                DrawCenteredLabel(context, rowRect, "+ Row");

                _tableHitRegions.Add(new TableHitRegion
                {
                    Rect = rowRect,
                    Group = group,
                    Type = TableHitType.AddRow
                });

                // + Col (to the right)
                double colBtnWidth = 32;
                var colRect = new Rect(xRight + 2, yTop, colBtnWidth, yBottom - yTop);
                var colBg = new SolidColorBrush(Color.FromArgb(60, 80, 80, 160));
                var colBorder = new Pen(new SolidColorBrush(Color.FromArgb(120, 120, 120, 200)), 1);
                context.FillRectangle(colBg, colRect);
                context.DrawRectangle(colBorder, colRect);
                DrawCenteredLabel(context, colRect, "+ Col");

                _tableHitRegions.Add(new TableHitRegion
                {
                    Rect = colRect,
                    Group = group,
                    Type = TableHitType.AddColumn
                });
            }
        }

        private void DrawCenteredLabel(DrawingContext context, Rect rect, string label)
        {
            var ft = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _baseTypeface,
                BaseFontSize * 0.85,
                Brushes.White);

            double x = rect.X + (rect.Width - ft.Width) / 2.0;
            double y = rect.Y + (rect.Height - ft.Height) / 2.0;
            context.DrawText(ft, new Point(x, y));
        }

        // ================== INLINE MARKDOWN ==================

        private double GetHeadingFontSize(int level)
        {
            return level switch
            {
                1 => BaseFontSize * 1.6,
                2 => BaseFontSize * 1.4,
                3 => BaseFontSize * 1.25,
                4 => BaseFontSize * 1.15,
                _ => BaseFontSize * 1.05
            };
        }

        private Typeface GetTypeface(bool bold, bool italic)
        {
            var style = italic ? FontStyle.Italic : FontStyle.Normal;
            var weight = bold ? FontWeight.Bold : FontWeight.Normal;
            return new Typeface(_baseTypeface.FontFamily, style, weight);
        }

        private List<RunSegment> ParseInlineMarkdown(string text)
        {
            var result = new List<RunSegment>();
            var current = new RunSegment();

            bool bold = false;
            bool italic = false;
            bool code = false;

            int i = 0;
            while (i < text.Length)
            {
                // bold: **text**
                if (!code && i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    if (current.Text.Length > 0)
                    {
                        current.Bold = bold;
                        current.Italic = italic;
                        current.Code = code;
                        result.Add(current);
                        current = new RunSegment();
                    }

                    bold = !bold;
                    i += 2;
                    continue;
                }

                // italic: *text*
                if (!code && text[i] == '*')
                {
                    if (i + 1 < text.Length && text[i + 1] == '*')
                    {
                        // handled above
                    }
                    else
                    {
                        if (current.Text.Length > 0)
                        {
                            current.Bold = bold;
                            current.Italic = italic;
                            current.Code = code;
                            result.Add(current);
                            current = new RunSegment();
                        }

                        italic = !italic;
                        i++;
                        continue;
                    }
                }

                // inline code: `code`
                if (text[i] == '`')
                {
                    if (current.Text.Length > 0)
                    {
                        current.Bold = bold;
                        current.Italic = italic;
                        current.Code = code;
                        result.Add(current);
                        current = new RunSegment();
                    }

                    code = !code;
                    i++;
                    continue;
                }

                current.Text += text[i];
                i++;
            }

            if (current.Text.Length > 0)
            {
                current.Bold = bold;
                current.Italic = italic;
                current.Code = code;
                result.Add(current);
            }

            return result;
        }

        // ================== TABLE CELL / CARET HELPERS ==================

        private bool TryGetTableCellFromCaret(
            int lineIndex,
            int caretCol,
            out TableRowRef rowRef,
            out int cellIndex,
            out int cellStart,
            out int cellEnd)
        {
            rowRef = null;
            cellIndex = -1;
            cellStart = 0;
            cellEnd = 0;

            if (!_tableRowByLine.TryGetValue(lineIndex, out rowRef))
                return false;

            var row = rowRef.Group.Rows[rowRef.RowIndex];
            if (row.IsSeparator)
                return false;

            string line = Document.Lines[lineIndex] ?? string.Empty;

            var pipePositions = new List<int>();
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '|')
                    pipePositions.Add(i);

            if (pipePositions.Count < 2)
                return false;

            // Cells are between consecutive pipes
            for (int idx = 0; idx < pipePositions.Count - 1; idx++)
            {
                int p0 = pipePositions[idx];
                int p1 = pipePositions[idx + 1];

                int start = p0 + 1;
                int end = p1; // exclusive

                if (caretCol < start)
                {
                    // caret is left of this cell, snap to this one
                    cellIndex = idx;
                    cellStart = start;
                    cellEnd = end;
                    return true;
                }

                if (caretCol >= start && caretCol <= end)
                {
                    cellIndex = idx;
                    cellStart = start;
                    cellEnd = end;
                    return true;
                }
            }

            // Right of last cell: snap to last cell
            int lastIdx = pipePositions.Count - 2;
            cellIndex = lastIdx;
            cellStart = pipePositions[lastIdx] + 1;
            cellEnd = pipePositions[lastIdx + 1];
            return true;
        }

        private bool TryGetTableCellFromX(
            int lineIndex,
            double x,
            out TableRowRef rowRef,
            out int cellIndex,
            out int cellStart,
            out int cellEnd)
        {
            rowRef = null;
            cellIndex = -1;
            cellStart = 0;
            cellEnd = 0;

            if (!_tableRowByLine.TryGetValue(lineIndex, out rowRef))
                return false;

            var group = rowRef.Group;
            var row = group.Rows[rowRef.RowIndex];
            if (row.IsSeparator)
                return false;

            string line = Document.Lines[lineIndex] ?? string.Empty;

            // Choose column by x relative to table
            double localX = x - LeftPadding;
            if (localX < 0) localX = 0;

            int chosenCol = 0;
            double accum = 0;
            for (int c = 0; c < group.ColumnWidths.Length; c++)
            {
                double w = group.ColumnWidths[c];
                if (localX < accum + w)
                {
                    chosenCol = c;
                    break;
                }
                accum += w;
                if (c == group.ColumnWidths.Length - 1)
                    chosenCol = c;
            }

            // Map chosenCol to text cell boundaries
            var pipePositions = new List<int>();
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '|')
                    pipePositions.Add(i);

            if (pipePositions.Count < 2)
                return false;

            if (chosenCol >= pipePositions.Count - 1)
                chosenCol = pipePositions.Count - 2;

            int p0 = pipePositions[chosenCol];
            int p1 = pipePositions[chosenCol + 1];
            cellIndex = chosenCol;
            cellStart = p0 + 1;
            cellEnd = p1;

            return true;
        }

        private double GetTableCellLeftX(TableRowRef rowRef, int cellIndex)
        {
            double x = LeftPadding;
            for (int i = 0; i < cellIndex; i++)
                x += rowRef.Group.ColumnWidths[i];

            return x + TableCellPaddingX;
        }

        private bool IsCaretAtTableCellBoundary(out string line, out int cellStart, out int cellEnd)
        {
            line = null;
            cellStart = 0;
            cellEnd = 0;

            int lineIndex = Document.CaretLine;
            int col = Document.CaretColumn;

            if (!TryGetTableCellFromCaret(lineIndex, col,
                    out _, out _, out cellStart, out cellEnd))
                return false;

            line = Document.Lines[lineIndex] ?? string.Empty;
            return true;
        }

        // ================== CARET ==================

        private void DrawCaret(DrawingContext context, double lineHeight)
        {
            int caretLine = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            if (caretLine < 0 || caretLine >= Document.Lines.Count)
                return;

            string lineText = Document.Lines[caretLine] ?? string.Empty;
            caretCol = Math.Clamp(caretCol, 0, lineText.Length);

            int visualIndex = 0;
            VisualLine caretVisual = null;

            for (int i = 0; i < _visualLines.Count; i++)
            {
                var v = _visualLines[i];
                if (v.DocLineIndex != caretLine)
                    continue;

                int start = v.StartColumn;
                int end = start + v.Length;

                if (caretCol >= start && caretCol <= end)
                {
                    caretVisual = v;
                    visualIndex = i;
                    break;
                }

                if (caretCol == lineText.Length && end == lineText.Length)
                {
                    caretVisual = v;
                    visualIndex = i;
                }
            }

            if (caretVisual == null)
                return;

            int colInSeg = Math.Max(0, caretCol - caretVisual.StartColumn);

            double x;
            double yTop = visualIndex * lineHeight;
            double yBottom = yTop + lineHeight;

            // Special handling for table rows: caret x is inside active cell and clamped to cell width
            if (_tableRowByLine.TryGetValue(caretLine, out var rowRef) &&
                TryGetTableCellFromCaret(caretLine, caretCol,
                    out _, out var cellIndex, out var cellStart, out var cellEnd))
            {
                var row = rowRef.Group.Rows[rowRef.RowIndex];

                // Raw text of this cell
                string rawCell = lineText.Substring(cellStart, Math.Max(0, cellEnd - cellStart));
                int caretInCell = Math.Clamp(caretCol - cellStart, 0, rawCell.Length);

                const string BrToken = "<br>";

                // Figure out:
                //  - subLineIndex: which visual sub-line we're on
                //  - currentLineBeforeCaret: text from start of this sub-line to caret
                int subLineIndex = 0;
                int scan = 0;
                int lastBrPos = -1;
                while (true)
                {
                    int brIdx = rawCell.IndexOf(BrToken, scan, StringComparison.Ordinal);
                    if (brIdx < 0 || caretInCell <= brIdx)
                        break;

                    subLineIndex++;
                    lastBrPos = brIdx;
                    scan = brIdx + BrToken.Length;
                }

                string currentLineBeforeCaret;
                if (lastBrPos >= 0)
                {
                    int startOfCurrentLine = lastBrPos + BrToken.Length;
                    int len = Math.Max(0, caretInCell - startOfCurrentLine);
                    currentLineBeforeCaret = len > 0 ? rawCell.Substring(startOfCurrentLine, len) : string.Empty;
                }
                else
                {
                    currentLineBeforeCaret = caretInCell > 0 ? rawCell.Substring(0, caretInCell) : string.Empty;
                }

                // X: measure only the text on the current sub-line (no <br> spaces)
                double cellLeftX = GetTableCellLeftX(rowRef, cellIndex);
                double w = MeasureTextWidth(currentLineBeforeCaret, BaseFontSize);

                double cellWidth = rowRef.Group.ColumnWidths[cellIndex];
                double maxContentWidth = Math.Max(0, cellWidth - TableCellPaddingX * 2);
                if (w > maxContentWidth)
                    w = maxContentWidth;

                x = cellLeftX + w;

                // Y: align to the correct sub-line in this row
                int baseVisIndex = visualIndex;
                for (int i = visualIndex; i >= 0; i--)
                {
                    var v = _visualLines[i];
                    if (v.TableRef != null &&
                        ReferenceEquals(v.TableRef.Group, rowRef.Group) &&
                        v.TableRef.RowIndex == rowRef.RowIndex &&
                        v.TableVisualIndex == 0)
                    {
                        baseVisIndex = i;
                        break;
                    }
                }

                yTop    = (baseVisIndex + subLineIndex) * lineHeight;
                yBottom = yTop + lineHeight;
            }
            else
            {
                string prefix = (colInSeg > 0 && caretVisual.StartColumn + colInSeg <= lineText.Length)
                    ? lineText.Substring(caretVisual.StartColumn, colInSeg)
                    : string.Empty;

                x = LeftPadding + MeasureTextWidth(prefix, BaseFontSize);
            }


            var caretPen = new Pen(Brushes.White, 1);
            context.DrawLine(caretPen, new Point(x, yTop), new Point(x, yBottom));
        }

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


        // ================== POINTER / HIT TEST ==================

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            Focus();

            var p = e.GetPosition(this);

            // Table UI first
            foreach (var hit in _tableHitRegions)
            {
                if (hit.Rect.Contains(p))
                {
                    HandleTableHit(hit);
                    e.Handled = true;
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
                TryGetTableCellFromX(vis.DocLineIndex, p.X, out var rowRef,
                    out _, out var cellStart, out var cellEnd))
            {
                // Place caret at end of visible text in that cell (or at cell start if empty)
                int caretCol = cellStart;
                int textEnd = Math.Min(cellEnd, rawLine.Length);
                for (int i = textEnd - 1; i >= cellStart && i < rawLine.Length; i--)
                {
                    if (!char.IsWhiteSpace(rawLine[i]))
                    {
                        caretCol = i + 1;
                        break;
                    }
                }

                Document.SetCaret(vis.DocLineIndex, caretCol);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // Normal line: approximate column from x
            int col = GetColumnFromX(rawLine, vis.StartColumn, vis.Length, p.X - LeftPadding);

            Document.SetCaret(vis.DocLineIndex, col);
            NormalizeCaretAroundBr();
            InvalidateVisual();

            e.Handled = true;
        }

        private void HandleTableHit(TableHitRegion hit)
        {
            switch (hit.Type)
            {
                case TableHitType.AddRow:
                    AddTableRow(hit.Group);
                    break;
                case TableHitType.AddColumn:
                    AddTableColumn(hit.Group);
                    break;
            }
        }

        // Add row at end (used by +Row button)
        private void AddTableRow(TableGroup group)
        {
            // Last non-separator row
            int lastNonSepIndex = -1;
            for (int i = 0; i < group.Rows.Count; i++)
            {
                if (!group.Rows[i].IsSeparator)
                    lastNonSepIndex = i;
            }

            if (lastNonSepIndex == -1)
                return;

            var lastRow = group.Rows[lastNonSepIndex];
            int colCount = group.ColumnWidths.Length;

            string newLine = BuildTableLine(colCount, isSeparator: false);

            int insertLineIndex = lastRow.LineIndex + 1;
            if (insertLineIndex < 0 || insertLineIndex > Document.Lines.Count)
                insertLineIndex = Document.Lines.Count;

            Document.Lines.Insert(insertLineIndex, newLine);

            // Caret inside first cell
            int caretCol = newLine.IndexOf(' ') + 1;
            if (caretCol < 0) caretCol = newLine.Length;

            Document.SetCaret(insertLineIndex, caretCol);
            _text = Document.GetText();
            InvalidateVisual();
        }

        // Add row below current caret row in same table (used for Enter in table)
        private void AddTableRowBelowCaret(TableRowRef rowRef, int cellIndex)
        {
            int colCount = rowRef.Group.ColumnWidths.Length;
            string newLine = BuildTableLine(colCount, isSeparator: false);

            int insertLineIndex = rowRef.Group.Rows[rowRef.RowIndex].LineIndex + 1;
            if (insertLineIndex < 0 || insertLineIndex > Document.Lines.Count)
                insertLineIndex = Document.Lines.Count;

            Document.Lines.Insert(insertLineIndex, newLine);

            // Place caret in the same column (cellIndex) in the new row
            string line = newLine;
            var pipePositions = new List<int>();
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '|')
                    pipePositions.Add(i);

            int caretCol;
            if (pipePositions.Count >= 2 && cellIndex >= 0 && cellIndex < pipePositions.Count - 1)
            {
                int p0 = pipePositions[cellIndex];
                int p1 = pipePositions[cellIndex + 1];
                int start = p0 + 1;

                caretCol = start;
                if (caretCol < line.Length && line[caretCol] == ' ')
                    caretCol++;
            }
            else
            {
                caretCol = newLine.IndexOf(' ') + 1;
                if (caretCol < 0) caretCol = newLine.Length;
            }

            Document.SetCaret(insertLineIndex, caretCol);
            _text = Document.GetText();
            InvalidateVisual();
        }

        private void AddTableColumn(TableGroup group)
        {
            int currentColCount = group.ColumnWidths.Length;
            int newColCount = currentColCount + 1;

            // Update rows
            foreach (var row in group.Rows)
            {
                var newCells = new List<string>(newColCount);
                newCells.AddRange(row.Cells);

                if (newCells.Count < newColCount)
                    newCells.Add(row.IsSeparator ? "---" : string.Empty);
                else
                    newCells.Add(row.IsSeparator ? "---" : string.Empty);

                string newLine = BuildTableLineFromCells(newCells);
                Document.Lines[row.LineIndex] = newLine;
            }

            // Caret into header's new column (first non-separator row)
            int headerLineIndex = -1;
            foreach (var row in group.Rows)
            {
                if (!row.IsSeparator)
                {
                    headerLineIndex = row.LineIndex;
                    break;
                }
            }

            if (headerLineIndex >= 0)
            {
                string headerLine = Document.Lines[headerLineIndex];
                int caretCol = headerLine.Length - 2;
                if (caretCol < 0) caretCol = headerLine.Length;
                Document.SetCaret(headerLineIndex, caretCol);
            }

            _text = Document.GetText();
            InvalidateVisual();
        }

        private string BuildTableLine(int colCount, bool isSeparator)
        {
            var cells = new List<string>(colCount);
            for (int i = 0; i < colCount; i++)
                cells.Add(isSeparator ? "---" : string.Empty);

            return BuildTableLineFromCells(cells);
        }

        private string BuildTableLineFromCells(List<string> cells)
        {
            var sb = new StringBuilder();
            sb.Append("|");
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                    sb.Append(" |");
                sb.Append(' ');
                sb.Append(cells[i]);
                sb.Append(' ');
            }
            sb.Append(" |");
            return sb.ToString();
        }

        private int GetColumnFromX(string rawLine, int segStart, int segLength, double x)
        {
            if (x <= 0)
                return segStart;

            int maxCol = Math.Min(rawLine.Length, segStart + segLength);
            int bestCol = segStart;
            double bestDiff = double.MaxValue;

            for (int col = segStart; col <= maxCol; col++)
            {
                string prefix = rawLine.Substring(segStart, col - segStart);
                double width = MeasureTextWidth(prefix, BaseFontSize);
                double diff = Math.Abs(width - x);

                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestCol = col;
                }
            }

            return bestCol;
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
                    break;
                case Key.End:
                    Document.MoveCaretToLineEnd();
                    break;
                case Key.Enter:
                    {
                        // In tables:
                        //  - Shift+Enter => insert <br> inside current cell (multiline cell)
                        //  - Enter       => add a new row below in the same column
                        if (_tableRowByLine.TryGetValue(Document.CaretLine, out var tr) &&
                            !tr.Group.Rows[tr.RowIndex].IsSeparator)
                        {
                            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                            {
                                Document.InsertText("<br>");
                                _text = Document.GetText();
                            }
                            else
                            {
                                if (TryGetTableCellFromCaret(Document.CaretLine, Document.CaretColumn,
                                        out _, out var cellIndex, out _, out _))
                                {
                                    AddTableRowBelowCaret(tr, cellIndex);
                                }
                                else
                                {
                                    AddTableRowBelowCaret(tr, 0);
                                }
                            }
                        }
                        else
                        {
                            Document.InsertNewLine();
                            _text = Document.GetText();
                        }
                        break;
                    }
                case Key.Back:
                {
                    // First: special case for <br> in table cells
                    if (_tableRowByLine.ContainsKey(Document.CaretLine))
                    {
                        string line = Document.Lines[Document.CaretLine] ?? string.Empty;
                        int col = Document.CaretColumn;
                        const string BrToken = "<br>";

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
                    // First: special case for <br> in table cells
                    if (_tableRowByLine.ContainsKey(Document.CaretLine))
                    {
                        string line = Document.Lines[Document.CaretLine] ?? string.Empty;
                        int col = Document.CaretColumn;
                        const string BrToken = "<br>";

                        if (col + BrToken.Length <= line.Length &&
                            line.Substring(col, BrToken.Length) == BrToken)
                        {
                            // Remove whole <br>, caret stays at same column
                            string newLine = line.Remove(col, BrToken.Length);
                            Document.Lines[Document.CaretLine] = newLine;
                            Document.SetCaret(Document.CaretLine, col);
                            _text = Document.GetText();
                            InvalidateVisual();
                            e.Handled = true;
                            return;
                        }
                    }

                    // Prevent delete from deleting table pipes at right cell border
                    if (IsCaretAtTableCellBoundary(out var line2, out _, out var cellEnd))
                    {
                        if (Document.CaretColumn >= cellEnd &&
                            Document.CaretColumn < line2.Length &&
                            line2[Document.CaretColumn] == '|')
                        {
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
    }
}
