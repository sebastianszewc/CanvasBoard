using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CanvasBoard.App.Markdown.Document;
using CanvasBoard.App.Markdown.Tables;
using CanvasBoard.App.Markdown.Model;
using System.Linq;


namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);

            EnsureLayout();

            // Background
            var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.FillRectangle(_backgroundBrush, rect);

            double baseLineHeight = LineHeight;
            double y = 0.0;

            for (int i = 0; i < _visualLines.Count; i++)
            {
                var vis = _visualLines[i];
                int lineIndex = vis.DocLineIndex;
                bool isCaretLine = (lineIndex == Document.CaretLine);

                // RESET per iteration
                double lineHeight = baseLineHeight;

                if (!isCaretLine)
                {
                    int level = GetHeadingLevelForLine(lineIndex);
                    if (level > 0 && level < HeadingLineHeightFactors.Length)
                    {
                        lineHeight = baseLineHeight * HeadingLineHeightFactors[level];
                    }
                }

                DrawSelectionForVisualLine(context, vis, y, lineHeight);

                string rawLine = Document.Lines[lineIndex] ?? string.Empty;

                var tableBlock = FindTableBlockForLine(lineIndex);
                if (tableBlock != null)
                {
                    // Alignment row in markdown: the line right after header
                    bool isAlignmentRow = (lineIndex == tableBlock.Table.StartLine + 1);

                    // For table lines we ignore heading scaling and just use base height
                    double rowHeight = baseLineHeight;

                    // Draw selection background (alignment row has zero height)
                    double selectionHeight = isAlignmentRow ? 0.0 : rowHeight;
                    DrawSelectionForVisualLine(context, vis, y, selectionHeight);

                    // Only once per table: draw the entire grid starting from the header line
                    if (vis.IsFirstSegmentOfLogicalLine && lineIndex == tableBlock.Table.StartLine)
                    {
                        DrawTableFromModel(context, tableBlock, y, rowHeight);
                    }

                    // Vertical layout:
                    //  - header & body lines advance by rowHeight
                    //  - alignment row has zero height (no gap between header and first body row)
                    if (!isAlignmentRow)
                        y += rowHeight;

                    continue;
                }

                if (!isCaretLine && IsHorizontalRuleLine(rawLine))
                {
                    if (vis.IsFirstSegmentOfLogicalLine)
                        DrawHorizontalRule(context, y, lineHeight);

                    y += lineHeight;
                    continue;
                }

                DrawSegment(context, rawLine, vis, y, lineHeight);
                y += lineHeight;
            }

            if (IsFocused)
                DrawCaret(context, baseLineHeight);
        }

        private void DrawSegment(
            DrawingContext context,
            string rawLine,
            VisualLine vis,
            double y,
            double lineHeight)
        {
            // Caret line: raw/plain rendering, no inline styling
            if (vis.DocLineIndex == Document.CaretLine)
            {
                string segmentText = string.Empty;

                if (!string.IsNullOrEmpty(rawLine))
                {
                    int start = vis.StartColumn;
                    int len = vis.Length;

                    if (start >= 0 && start < rawLine.Length && len > 0)
                    {
                        if (start + len > rawLine.Length)
                            len = rawLine.Length - start;

                        segmentText = rawLine.Substring(start, len);
                    }
                }

                string displayPlain = segmentText.Replace("\t", new string(' ', TabSize));

                var ftPlain = new FormattedText(
                    displayPlain,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    GetTypefaceForLineKind(MarkdownLineKind.Normal),
                    BaseFontSize,
                    _foregroundBrush);

                context.DrawText(ftPlain, new Point(LeftPadding, y));
                return;
            }

            // ---------- Non-caret lines ----------

            var lineKind = GetLineKind(vis.DocLineIndex);
            string lineText = rawLine ?? string.Empty;

            // Heading info
            int headingLevel = GetHeadingLevelForLine(vis.DocLineIndex);
            bool isHeading = headingLevel > 0;

            if (isHeading)
            {
                // Only draw on the first visual segment
                if (!vis.IsFirstSegmentOfLogicalLine)
                    return;

                // Skip leading '#' and whitespace
                int i = 0;
                int len = lineText.Length;
                while (i < len && lineText[i] == '#')
                    i++;
                while (i < len && char.IsWhiteSpace(lineText[i]))
                    i++;

                string headingText = (i < len) ? lineText.Substring(i) : string.Empty;
                headingText = headingText.Replace("\t", new string(' ', TabSize));

                // Bigger font for headings
                double fontSize = BaseFontSize;
                if (headingLevel > 0 && headingLevel < HeadingFontSizeOffsets.Length)
                    fontSize = BaseFontSize + HeadingFontSizeOffsets[headingLevel];

                // Vertical offset within this line's height to create more space
                double yOffset = y + lineHeight * 0.15;

                var ftHeading = new FormattedText(
                    headingText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    GetTypefaceForLineKind(lineKind),
                    fontSize,
                    GetBrushForLineKind(lineKind));

                context.DrawText(ftHeading, new Point(LeftPadding, yOffset));
                return;
            }

            // Non-heading lines: inline styling as before
            var inlineRuns = GetInlineRunsForLine(vis.DocLineIndex);
            double baseFontSize = BaseFontSize;

            if (inlineRuns == null || inlineRuns.Count == 0)
            {
                // Fallback: draw as plain with block-level style
                string segmentText = string.Empty;

                if (!string.IsNullOrEmpty(rawLine))
                {
                    int start = vis.StartColumn;
                    int len = vis.Length;

                    if (start >= 0 && start < rawLine.Length && len > 0)
                    {
                        if (start + len > rawLine.Length)
                            len = rawLine.Length - start;

                        segmentText = rawLine.Substring(start, len);
                    }
                }

                string display = segmentText.Replace("\t", new string(' ', TabSize));

                var ft = new FormattedText(
                    display,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    GetTypefaceForLineKind(lineKind),
                    baseFontSize,
                    GetBrushForLineKind(lineKind));

                context.DrawText(ft, new Point(LeftPadding, y));
                return;
            }

            double cw = GetCharWidth();
            int segStart = vis.StartColumn;
            int segEnd = segStart + vis.Length;

            if (segStart < 0) segStart = 0;
            if (segEnd > lineText.Length) segEnd = lineText.Length;
            if (segEnd <= segStart)
                return;

            foreach (var r in inlineRuns)
            {
                int runStart = r.StartColumn;
                int runEnd = runStart + r.Length;

                int partStart = System.Math.Max(segStart, runStart);
                int partEnd = System.Math.Min(segEnd, runEnd);

                if (partEnd <= partStart)
                    continue;

                int lengthChars = partEnd - partStart;
                if (lengthChars <= 0)
                    continue;

                string rawSlice = lineText.Substring(partStart, lengthChars);
                string displaySlice = rawSlice.Replace("\t", new string(' ', TabSize));

                int colsBefore = ComputeColumns(lineText, segStart, partStart - segStart);
                double x = LeftPadding + colsBefore * cw;

                var (typeface, brush) = GetStyleForInline(lineKind, r.Styles);

                var ft = new FormattedText(
                    displaySlice,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    baseFontSize,
                    brush);

                context.DrawText(ft, new Point(x, y));
            }
        }

        private void DrawSelectionForVisualLine(
            DrawingContext context,
            VisualLine vis,
            double y,
            double lineHeight)
        {
            if (!TryGetSelectionSpan(out var span))
                return;

            int sl = span.StartLine;
            int sc = span.StartColumn;
            int el = span.EndLine;
            int ec = span.EndColumn;

            int lineIndex = vis.DocLineIndex;
            if (lineIndex < sl || lineIndex > el)
                return;

            string line = Document.Lines[lineIndex] ?? string.Empty;
            int lineLen = line.Length;

            int lineSelStart;
            int lineSelEnd;

            if (sl == el)
            {
                lineSelStart = sc;
                lineSelEnd = ec;
            }
            else
            {
                if (lineIndex == sl)
                {
                    lineSelStart = sc;
                    lineSelEnd = lineLen;
                }
                else if (lineIndex == el)
                {
                    lineSelStart = 0;
                    lineSelEnd = ec;
                }
                else
                {
                    lineSelStart = 0;
                    lineSelEnd = lineLen;
                }
            }

            lineSelStart = System.Math.Clamp(lineSelStart, 0, lineLen);
            lineSelEnd = System.Math.Clamp(lineSelEnd, 0, lineLen);
            if (lineSelEnd <= lineSelStart)
                return;

            int segStart = vis.StartColumn;
            int segEnd = segStart + vis.Length;

            // Intersection of [lineSelStart, lineSelEnd) with [segStart, segEnd)
            int startChar = System.Math.Max(lineSelStart, segStart);
            int endChar = System.Math.Min(lineSelEnd, segEnd);

            if (endChar <= startChar)
                return;

            double cw = GetCharWidth();

            // Convert char indices to visual columns within this segment
            int startCols = ComputeColumns(line, segStart, startChar - segStart);
            int endCols = ComputeColumns(line, segStart, endChar - segStart);

            double xStart = LeftPadding + startCols * cw;
            double width = (endCols - startCols) * cw;

            var r = new Rect(xStart, y, width, lineHeight);
            context.FillRectangle(_selectionBrush, r);
        }

        private void DrawCaret(DrawingContext context, double baseLineHeight)
        {
            int caretLine = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            if (caretLine < 0 || caretLine >= Document.Lines.Count || _visualLines.Count == 0)
                return;

            string lineText = Document.Lines[caretLine] ?? string.Empty;
            caretCol = System.Math.Clamp(caretCol, 0, lineText.Length);

            // 1) Find visual segment containing the caret
            int caretVisualIndex = -1;
            VisualLine? caretVis = null;

            for (int i = 0; i < _visualLines.Count; i++)
            {
                var v = _visualLines[i];
                if (v.DocLineIndex != caretLine)
                    continue;

                int start = v.StartColumn;
                int end = start + v.Length;

                if ((v.Length == 0 && caretCol == 0) ||
                    (caretCol >= start && caretCol <= end))
                {
                    caretVisualIndex = i;
                    caretVis = v;
                    break;
                }
            }

            if (caretVis == null)
                return;

            // 2) Accumulate Y using same heading-aware heights as Render
            double y = 0.0;
            for (int i = 0; i < caretVisualIndex; i++)
            {
                var vis = _visualLines[i];
                int lineIndex = vis.DocLineIndex;

                double lineHeight = baseLineHeight;
                int level = GetHeadingLevelForLine(lineIndex);
                if (level > 0 && level < HeadingLineHeightFactors.Length)
                {
                    lineHeight = baseLineHeight * HeadingLineHeightFactors[level];
                }

                y += lineHeight;
            }

            // 3) Compute X within this segment (tabs-aware)
            double cw = GetCharWidth();
            int localStart = caretVis.StartColumn;
            int clampedCol = caretCol;
            if (clampedCol < localStart) clampedCol = localStart;
            if (clampedCol > localStart + caretVis.Length) clampedCol = localStart + caretVis.Length;

            int colsFromSegStart = ComputeColumns(lineText, localStart, clampedCol - localStart);
            double x = LeftPadding + colsFromSegStart * cw;

            // 4) Draw caret with base height
            var caretRect = new Rect(x, y, 1.0, baseLineHeight);
            context.FillRectangle(_foregroundBrush, caretRect);
        }


        // ----------------------------
        // Table rendering
        // ----------------------------
        private TableBlock? FindTableBlockForLine(int lineIndex)
        {
            if (_model == null)
                return null;

            foreach (var block in _model.Blocks)
            {
                if (block.Kind != MarkdownBlockKind.Table)
                    continue;

                if (lineIndex >= block.StartLine && lineIndex <= block.EndLine)
                    return (TableBlock)block;
            }

            return null;
        }    
    
        // ----------------------------
        // Table row rendering (model-based)
        // ----------------------------

        private void DrawTableFromModel(
            DrawingContext context,
            TableBlock tableBlock,
            double y,
            double rowHeight)
        {
            var table = tableBlock.Table;
            int rowCount = table.RowCount;
            int colCount = table.ColumnCount;

            if (rowCount <= 0 || colCount <= 0)
                return;

            double cw = GetCharWidth();

            // Compute column widths in characters (max text length per column + padding)
            var colWidthsChars = new double[colCount];
            for (int c = 0; c < colCount; c++)
            {
                int max = 1;
                for (int r = 0; r < rowCount; r++)
                {
                    string cellText = table.GetCell(r, c) ?? string.Empty;
                    if (cellText.Length > max)
                        max = cellText.Length;
                }

                // Inner padding of 2 "characters"
                colWidthsChars[c] = max + 2;
            }

            var colWidthsPx = new double[colCount];
            for (int c = 0; c < colCount; c++)
                colWidthsPx[c] = colWidthsChars[c] * cw;

            double xStart = LeftPadding;
            var borderPen = new Pen(_foregroundBrush, 0.5);

            for (int r = 0; r < rowCount; r++)
            {
                double rowTop = y + r * rowHeight;
                double x = xStart;

                for (int c = 0; c < colCount; c++)
                {
                    double cellWidth = colWidthsPx[c];
                    var cellRect = new Rect(x, rowTop, cellWidth, rowHeight);

                    // Cell border – rows share borders, so the grid is visually joined
                    context.DrawRectangle(null, borderPen, cellRect);

                    string cellText = table.GetCell(r, c) ?? string.Empty;

                    var ft = new FormattedText(
                        cellText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        GetTypefaceForLineKind(r == 0 ? MarkdownLineKind.Heading : MarkdownLineKind.Normal),
                        BaseFontSize,
                        _foregroundBrush);

                    // Approximate metrics – good enough for alignment
                    double textWidthApprox = cw * System.Math.Max(1, cellText.Length);
                    double textHeightApprox = BaseFontSize;

                    // Horizontal alignment per column
                    double textX;
                    switch (table.Alignments[c])
                    {
                        case TableAlignment.Right:
                            textX = x + cellWidth - textWidthApprox - cw; // right + padding
                            break;
                        case TableAlignment.Center:
                            textX = x + (cellWidth - textWidthApprox) / 2.0;
                            break;
                        default: // Left
                            textX = x + cw; // left padding
                            break;
                    }

                    // Vertical centering
                    double textY = rowTop + (rowHeight - textHeightApprox) / 2.0;

                    context.DrawText(ft, new Point(textX, textY));

                    x += cellWidth;
                }
            }
        }

        // ----------------------------
        // Horizontal rule rendering
        // ----------------------------

        private static bool IsHorizontalRuleLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return false;

            var trimmed = rawLine.Trim();

            if (trimmed.Length < 3)
                return false;

            // HR patterns: --- *** ___ (3 or more repeated)
            char c = trimmed[0];
            if (c != '-' && c != '*' && c != '_')
                return false;

            // All chars must be the same symbol
            if (!trimmed.All(ch => ch == c))
                return false;

            return true;
        }

        private void DrawHorizontalRule(DrawingContext context, double y, double lineHeight)
        {
            // Draw a single thin line across, centered in the line's vertical space
            double margin = 8.0;
            double centerY = y + lineHeight / 2.0;

            var pen = new Pen(_foregroundBrush, 1.0);

            context.DrawLine(
                pen,
                new Point(margin, centerY),
                new Point(Bounds.Width - margin, centerY));
        }    

        // ----------------------------
        // Heading lookup
        // ----------------------------

        private int GetHeadingLevelForLine(int lineIndex)
        {
            if (_model == null)
                return 0;

            foreach (var block in _model.Blocks)
            {
                if (block.Kind != MarkdownBlockKind.Heading)
                    continue;

                if (lineIndex >= block.StartLine && lineIndex <= block.EndLine)
                {
                    var hb = (HeadingBlock)block;
                    return hb.Level;
                }
            }

            return 0;
        }
    }
}
