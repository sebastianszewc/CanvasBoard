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

            int maxColumns = (int)(textWidth / cw);
            if (maxColumns <= 0)
                return 1;

            int bestChars = 0;
            int col = 0;
            int lineLen = line.Length;
            int endLimit = Math.Min(start + maxLen, lineLen);

            // Greedily add characters while we have columns left
            for (int idx = start; idx < endLimit; idx++)
            {
                char ch = line[idx];
                if (ch == '\t')
                {
                    col = ((col / TabSize) + 1) * TabSize;
                }
                else
                {
                    col++;
                }

                if (col > maxColumns)
                    break;

                bestChars++;
            }

            if (bestChars <= 0)
                bestChars = Math.Min(maxLen, 1);

            // Word-aware backoff: avoid cutting a word if possible
            int globalEndIndex = start + bestChars;
            if (globalEndIndex < lineLen && !char.IsWhiteSpace(line[globalEndIndex]))
            {
                int lastSpace = line.LastIndexOf(' ', globalEndIndex - 1, bestChars);
                if (lastSpace > start)
                {
                    bestChars = lastSpace - start;
                }
            }

            if (bestChars <= 0)
                bestChars = Math.Min(maxLen, 1);

            return bestChars;
        }
    }
}
