using System;
using System.Collections.Generic;
using System.Globalization;
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

        // --------- Table block structures ---------

        private sealed class TableRow
        {
            public int LineIndex;
            public List<string> Cells = new();
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

            public TableRowRef TableRef; // null if not in a table
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

            for (int i = 0; i < _visualLines.Count; i++)
            {
                var vis = _visualLines[i];
                double y = i * lineHeight;

                string rawLine = Document.Lines[vis.DocLineIndex] ?? string.Empty;
                string segmentText = ExtractSegment(rawLine, vis.StartColumn, vis.Length);

                bool isActiveDocLine = IsFocused && vis.DocLineIndex == Document.CaretLine;

                if (isActiveDocLine)
                {
                    DrawPlainSegment(context, segmentText, y);
                }
                else
                {
                    DrawMarkdownSegment(context, vis, rawLine, segmentText, y);
                }
            }

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

                int headingLevel = 0;
                bool isBullet = false;
                bool isQuote = false;
                bool isHorizontalRule = false;

                if (!isInCodeBlock)
                {
                    // Heading (#) with leading spaces allowed
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

                    // Bullet (- / *) (if not heading)
                    if (headingLevel == 0)
                    {
                        var t = trimmedStart;
                        if (t.StartsWith("- ") || t.StartsWith("* "))
                            isBullet = true;
                    }

                    // Blockquote (>)
                    {
                        var t = trimmedStart;
                        if (t.StartsWith(">"))
                            isQuote = true;
                    }

                    // Horizontal rule: only - * _ and spaces, length >= 3, and not part of a table
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
                        TableRef = tableRef
                    });
                    continue;
                }

                // Table row: one visual line
                if (isTableLine && !isInCodeBlock && !isFenceLine)
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
                        IsHorizontalRule = false,
                        TableRef = tableRef
                    });
                    continue;
                }

                // Horizontal rule: single visual line
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
                        TableRef = null
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

                if (inCodeFence || isFenceLine)
                {
                    if (current != null)
                    {
                        FinalizeTableGroup(current);
                        current = null;
                    }
                    continue;
                }

                if (LooksLikeTableRow(rawLine, out var cells))
                {
                    if (current == null)
                    {
                        current = new TableGroup();
                        _tableGroups.Add(current);
                    }

                    current.Rows.Add(new TableRow
                    {
                        LineIndex = li,
                        Cells = cells
                    });
                }
                else
                {
                    if (current != null)
                    {
                        FinalizeTableGroup(current);
                        current = null;
                    }
                }
            }

            if (current != null)
                FinalizeTableGroup(current);

            // Fill lookup: line -> (group,rowIndex)
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

        private static bool LooksLikeTableRow(string rawLine, out List<string> cells)
        {
            cells = new List<string>();

            if (string.IsNullOrWhiteSpace(rawLine))
                return false;

            if (!rawLine.Contains("|"))
                return false;

            var parts = rawLine.Split('|');
            foreach (var part in parts)
            {
                var t = part.Trim();
                if (t.Length > 0)
                    cells.Add(t);
            }

            return cells.Count >= 2;
        }

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
            double cellPaddingX = 6;

            foreach (var row in group.Rows)
            {
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    string text = row.Cells[c];
                    double w = MeasureTextWidth(text, fontSize) + cellPaddingX * 2;
                    if (w > group.ColumnWidths[c])
                        group.ColumnWidths[c] = w;
                }
            }

            for (int c = 0; c < colCount; c++)
            {
                if (group.ColumnWidths[c] < 20)
                    group.ColumnWidths[c] = 20;
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

            // Horizontal rule: full-width line
            if (isHr)
            {
                DrawHorizontalRule(context, y);
                return;
            }

            // Table row: use block-level widths
            if (isTableLine)
            {
                DrawTableRow(context, vis.TableRef, y);
                return;
            }

            double x = LeftPadding;
            double fontSize = BaseFontSize;

            // Blockquote: bar + strip '>'
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

            // Code block lines: shaded mono, no inline parsing
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

        // --------- Table rendering using block layout ---------

        private void DrawTableRow(DrawingContext context, TableRowRef rowRef, double y)
        {
            var group = rowRef.Group;
            var row = group.Rows[rowRef.RowIndex];

            var cells = row.Cells;
            double[] colWidths = group.ColumnWidths;

            double fontSize = BaseFontSize;
            var typeface = _baseTypeface;
            var brush = Brushes.White;

            double cellPaddingX = 6;
            double cellPaddingY = 2;

            // Detect header separator row (e.g., --- or :---:)
            bool isSeparatorRow = true;
            foreach (var c in cells)
            {
                var t = c.Replace("-", "").Replace(":", "").Trim();
                if (t.Length != 0)
                {
                    isSeparatorRow = false;
                    break;
                }
            }

            double x = LeftPadding;

            if (isSeparatorRow)
            {
                double totalWidth = 0;
                int colCount = colWidths.Length;
                for (int c = 0; c < colCount; c++)
                    totalWidth += colWidths[c];

                var pen = new Pen(new SolidColorBrush(Color.FromArgb(160, 200, 200, 200)), 1);
                double yMid = y + fontSize * 0.8;
                context.DrawLine(pen, new Point(LeftPadding, yMid), new Point(LeftPadding + totalWidth, yMid));
                return;
            }

            var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 120, 120, 120)), 1);
            var bg = new SolidColorBrush(Color.FromArgb(40, 80, 80, 80));

            for (int c = 0; c < colWidths.Length; c++)
            {
                string cellText = c < cells.Count ? cells[c] : string.Empty;

                var ft = new FormattedText(
                    cellText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    brush);

                double cellWidth = colWidths[c];
                double cellHeight = ft.Height + cellPaddingY * 2;

                var rect = new Rect(x, y + fontSize * 0.15, cellWidth, cellHeight);
                context.FillRectangle(bg, rect);
                context.DrawRectangle(borderPen, rect);

                var textPos = new Point(x + cellPaddingX, y + fontSize * 0.2);
                context.DrawText(ft, textPos);

                x += cellWidth;
            }
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
                        // handled as bold above
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
            string prefix = (colInSeg > 0 && caretVisual.StartColumn + colInSeg <= lineText.Length)
                ? lineText.Substring(caretVisual.StartColumn, colInSeg)
                : string.Empty;

            double x = LeftPadding + MeasureTextWidth(prefix, BaseFontSize);
            double yTop = visualIndex * lineHeight;
            double yBottom = yTop + lineHeight;

            var caretPen = new Pen(Brushes.White, 1);
            context.DrawLine(caretPen, new Point(x, yTop), new Point(x, yBottom));
        }

        // ================== POINTER / HIT TEST ==================

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            Focus();
            EnsureLayout();

            var p = e.GetPosition(this);
            var lineHeight = BaseFontSize * LineSpacing;

            if (_visualLines.Count == 0)
                return;

            int visIndex = (int)(p.Y / lineHeight);
            visIndex = Math.Clamp(visIndex, 0, _visualLines.Count - 1);

            var vis = _visualLines[visIndex];
            string rawLine = Document.Lines[vis.DocLineIndex] ?? string.Empty;

            int col = GetColumnFromX(rawLine, vis.StartColumn, vis.Length, p.X - LeftPadding);

            Document.SetCaret(vis.DocLineIndex, col);
            InvalidateVisual();

            e.Handled = true;
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
                    break;
                case Key.Right:
                    Document.MoveCaretRight();
                    break;
                case Key.Up:
                    Document.MoveCaretUp();   // still logical, not visual-wrap-aware
                    break;
                case Key.Down:
                    Document.MoveCaretDown();
                    break;
                case Key.Home:
                    Document.MoveCaretToLineStart();
                    break;
                case Key.End:
                    Document.MoveCaretToLineEnd();
                    break;
                case Key.Enter:
                    Document.InsertNewLine();
                    break;
                case Key.Back:
                    Document.Backspace();
                    break;
                case Key.Delete:
                    Document.Delete();
                    break;
                default:
                    return;
            }

            _text = Document.GetText();
            InvalidateVisual();
            e.Handled = true;
        }
    }
}
