using System;
using Avalonia;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl
    {
        private void EnsureLayout()
        {
            _visualLines.Clear();

            double availableWidth = Bounds.Width;
            if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
                availableWidth = 1000;

            double textWidth = Math.Max(0, availableWidth - LeftPadding * 2);

            for (int li = 0; li < Document.Lines.Count; li++)
            {
                string rawLine = Document.Lines[li] ?? string.Empty;

                if (rawLine.Length == 0)
                {
                    _visualLines.Add(new VisualLine
                    {
                        DocLineIndex = li,
                        StartColumn = 0,
                        Length = 0,
                        IsFirstSegmentOfLogicalLine = true
                    });
                    continue;
                }

                int lineLen = rawLine.Length;
                int startIndex = 0;
                bool firstSegment = true;

                while (startIndex < lineLen)
                {
                    // For wrapped segments (not the first), skip leading spaces so
                    // new visual lines do not start with a space.
                    if (!firstSegment)
                    {
                        while (startIndex < lineLen && rawLine[startIndex] == ' ')
                            startIndex++;

                        if (startIndex >= lineLen)
                            break;
                    }

                    int remaining = lineLen - startIndex;

                    int maxLen = FindMaxFittingLength(rawLine, startIndex, remaining, textWidth);
                    if (maxLen <= 0)
                        maxLen = Math.Min(remaining, 1);

                    _visualLines.Add(new VisualLine
                    {
                        DocLineIndex = li,
                        StartColumn = startIndex,
                        Length = maxLen,
                        IsFirstSegmentOfLogicalLine = firstSegment
                    });

                    startIndex += maxLen;
                    firstSegment = false;
                }
            }
        }

        private int FindMaxFittingLength(string line, int start, int maxLen, double textWidth)
        {
            if (textWidth <= 0)
                return maxLen;

            double cw = GetCharWidth();
            if (cw <= 0)
                return maxLen;

            // Max characters that fit into the available width
            int maxChars = (int)(textWidth / cw);
            if (maxChars <= 0)
                return 1;

            int best = Math.Min(maxLen, maxChars);

            // Try to break at whitespace (word wrap) instead of mid-word
            int endIndex = start + best;
            if (endIndex < line.Length && !char.IsWhiteSpace(line[endIndex]))
            {
                int lastSpace = line.LastIndexOf(' ', endIndex - 1, best);
                if (lastSpace > start)
                {
                    best = lastSpace - start;
                }
            }

            if (best <= 0)
                best = Math.Min(maxLen, 1);

            return best;
        }
    }
}
