using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CanvasBoard.App.Markdown.Document;

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

            double lineHeight = LineHeight;

            for (int i = 0; i < _visualLines.Count; i++)
            {
                var vis = _visualLines[i];
                double y = i * lineHeight;

                // Selection background for this visual line (if any)
                DrawSelectionForVisualLine(context, vis, y, lineHeight);

                string rawLine = Document.Lines[vis.DocLineIndex] ?? string.Empty;

                // Draw this segment (styled or plain depending on caret line)
                DrawSegment(context, rawLine, vis, y);
            }

            if (IsFocused)
                DrawCaret(context, lineHeight);
        }

        private void DrawSegment(DrawingContext context, string rawLine, VisualLine vis, double y)
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

            // Non-caret lines: use inline styling
            var lineKind = GetLineKind(vis.DocLineIndex);
            var inlineRuns = GetInlineRunsForLine(vis.DocLineIndex);

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
                    BaseFontSize,
                    GetBrushForLineKind(lineKind));

                context.DrawText(ft, new Point(LeftPadding, y));
                return;
            }

            string lineText = rawLine ?? string.Empty;
            double cw = GetCharWidth();
            int segStart = vis.StartColumn;
            int segEnd = segStart + vis.Length;

            if (segStart < 0) segStart = 0;
            if (segEnd > lineText.Length) segEnd = lineText.Length;

            if (segEnd <= segStart)
                return;

            // Iterate through inline runs that intersect this visual segment
            foreach (var r in inlineRuns)
            {
                int runStart = r.StartColumn;
                int runEnd = runStart + r.Length;

                // Intersection with segment
                int partStart = System.Math.Max(segStart, runStart);
                int partEnd   = System.Math.Min(segEnd, runEnd);

                if (partEnd <= partStart)
                    continue;

                int lengthChars = partEnd - partStart;
                if (lengthChars <= 0)
                    continue;

                string rawSlice = lineText.Substring(partStart, lengthChars);
                string displaySlice = rawSlice.Replace("\t", new string(' ', TabSize));

                // Visual columns from segment start to this piece start
                int colsBefore = ComputeColumns(lineText, segStart, partStart - segStart);
                double x = LeftPadding + colsBefore * cw;

                var (typeface, brush) = GetStyleForInline(lineKind, r.Styles);

                var ft = new FormattedText(
                    displaySlice,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    BaseFontSize,
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
            int endChar   = System.Math.Min(lineSelEnd, segEnd);

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

        private void DrawCaret(DrawingContext context, double lineHeight)
        {
            int caretLine = Document.CaretLine;
            int caretCol = Document.CaretColumn;

            if (caretLine < 0 || caretLine >= Document.Lines.Count)
                return;

            string lineText = Document.Lines[caretLine] ?? string.Empty;
            caretCol = System.Math.Clamp(caretCol, 0, lineText.Length);

            // Find visual line segment where caret is
            int visualIndex = 0;
            VisualLine? caretVisual = null;

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

                // Handle empty line
                if (v.Length == 0 && caretCol == 0)
                {
                    caretVisual = v;
                    visualIndex = i;
                    break;
                }
            }

            if (caretVisual == null)
                return;

            double cw = GetCharWidth();

            // Visual columns from start of this segment to caret
            int colsFromSegStart = ComputeColumns(lineText, caretVisual.StartColumn, caretCol - caretVisual.StartColumn);

            double x = LeftPadding + colsFromSegStart * cw;
            double yTop = visualIndex * lineHeight;
            double yBottom = yTop + lineHeight;

            var caretPen = new Pen(Brushes.White, 1);
            context.DrawLine(caretPen, new Point(x, yTop), new Point(x, yBottom));
        }
    }
}
