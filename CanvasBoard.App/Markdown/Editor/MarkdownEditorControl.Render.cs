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

                // Always draw just this segment of the line; no special case for caret line
                DrawSegment(context, rawLine, vis, y);
            }

            if (IsFocused)
                DrawCaret(context, lineHeight);
        }

        private void DrawPlainLine(DrawingContext context, string line, double y)
        {
            var typeface = GetTypeface();

            var ft = new FormattedText(
                line,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                BaseFontSize,
                _foregroundBrush);

            context.DrawText(ft, new Point(LeftPadding, y));
        }

        private void DrawSegment(DrawingContext context, string rawLine, VisualLine vis, double y)
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

            var typeface = GetTypeface();

            var ft = new FormattedText(
                segmentText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                BaseFontSize,
                _foregroundBrush);

            context.DrawText(ft, new Point(LeftPadding, y));
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
            int start = System.Math.Max(lineSelStart, segStart);
            int end = System.Math.Min(lineSelEnd, segEnd);

            if (end <= start)
                return;

            double cw = GetCharWidth();

            double xStart = LeftPadding + (start - segStart) * cw;
            double width = (end - start) * cw;

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
            }

            if (caretVisual == null)
                return;

            // Offset in characters from the start of this visual segment
            int colInSeg = caretCol - caretVisual.StartColumn;
            double cw = GetCharWidth();

            double x = LeftPadding + cw * colInSeg;
            double yTop = visualIndex * lineHeight;
            double yBottom = yTop + lineHeight;

            var caretPen = new Pen(Brushes.White, 1);
            context.DrawLine(caretPen, new Point(x, yTop), new Point(x, yBottom));
        }
    }
}
