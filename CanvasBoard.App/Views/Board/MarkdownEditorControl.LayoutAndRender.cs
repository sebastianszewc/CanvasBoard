using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl : Control
    {
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
                                HeadingLevel = 0,
                                IsBullet = false,
                                IsQuote = false,
                                IsHorizontalRule = false,
                                TableRef = tableRef,
                                TableVisualIndex = v,
                                TableVisualCount = vCount
                            });
                        }
                    }
                    else
                    {
                        // Separator row is a single visual line
                        _visualLines.Add(new VisualLine
                        {
                            DocLineIndex = li,
                            StartColumn = 0,
                            Length = rawLine.Length,
                            IsFirstSegmentOfLogicalLine = true,
                            IsInCodeBlock = false,
                            IsFenceLine = false,
                            HeadingLevel = 0,
                            IsBullet = false,
                            IsQuote = false,
                            IsHorizontalRule = false,
                            TableRef = tableRef,
                            TableVisualIndex = 0,
                            TableVisualCount = 1
                        });
                    }

                    continue;
                }

                // Non-table lines: word-wrap into multiple visual segments
                int lineLen = rawLine.Length;
                if (lineLen == 0)
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
                        TableRef = null,
                        TableVisualIndex = 0,
                        TableVisualCount = 1
                    });
                    continue;
                }

                int remaining = lineLen;
                int start = 0;
                bool firstSeg = true;

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

        // ================== INLINE MARKDOWN HELPERS ==================

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
    }
}
