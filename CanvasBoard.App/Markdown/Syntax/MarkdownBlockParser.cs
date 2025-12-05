using System;
using System.Collections.Generic;
using CanvasBoard.App.Markdown.Document;
using CanvasBoard.App.Markdown.Model;
using CanvasBoard.App.Markdown.Tables;

namespace CanvasBoard.App.Markdown.Syntax
{
    /// <summary>
    /// Builds a high-level block model (MarkdownDocumentModel) from the
    /// underlying MarkdownDocument text. For now we:
    /// - Group tables into TableBlock (with per-cell spans).
    /// - Group all other regions into simple Paragraph blocks.
    /// </summary>
    public static class MarkdownBlockParser
    {
        public sealed class SimpleBlock : MarkdownBlock
        {
            public SimpleBlock(MarkdownBlockKind kind, TextSpan span, int startLine, int endLine)
                : base(kind, span, startLine, endLine)
            {
            }
        }

        public static MarkdownDocumentModel Parse(MarkdownDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var blocks = new List<MarkdownBlock>();
            var lines = document.Lines;
            int lineCount = lines.Count;

            // Parse all tables once
            var tables = TableParser.Parse(lines);
            int tableIdx = 0;

            int line = 0;
            while (line < lineCount)
            {
                // If current line is the start of the next table, emit a TableBlock
                if (tableIdx < tables.Count &&
                    line == tables[tableIdx].StartLine)
                {
                    var t = tables[tableIdx];

                    // Compute absolute span for the table (start of header to end of last row)
                    int startLine = t.StartLine;
                    int endLine = t.EndLine;

                    int startOffset = document.GetOffset(startLine, 0);
                    string lastLineText = lines[endLine] ?? string.Empty;
                    int endOffset = document.GetOffset(endLine, lastLineText.Length);

                    var span = new TextSpan(startOffset, endOffset);

                    // Build cell spans (header + body rows, excluding alignment row)
                    var cellSpans = BuildCellSpans(document, t);

                    var tableBlock = new TableBlock(
                        span,
                        startLine,
                        endLine,
                        t,
                        cellSpans);

                    blocks.Add(tableBlock);

                    // Skip past this table
                    line = endLine + 1;
                    tableIdx++;
                }
                else
                {
                    // Non-table region: collect consecutive non-table lines into one block
                    int startLine = line;

                    while (line < lineCount &&
                           !(tableIdx < tables.Count && line == tables[tableIdx].StartLine))
                    {
                        line++;
                    }

                    int endLine = line - 1;

                    if (endLine >= startLine)
                    {
                        int startOffset = document.GetOffset(startLine, 0);
                        string lastLineText = lines[endLine] ?? string.Empty;
                        int endOffset = document.GetOffset(endLine, lastLineText.Length);

                        var span = new TextSpan(startOffset, endOffset);

                        // For now, treat all non-table blocks as Paragraphs.
                        var block = new SimpleBlock(
                            MarkdownBlockKind.Paragraph,
                            span,
                            startLine,
                            endLine);

                        blocks.Add(block);
                    }
                }
            }

            return new MarkdownDocumentModel(blocks);
        }

        /// <summary>
        /// Build per-cell spans (TextSpan) for a table, based on the original document text.
        /// Only header + body rows are included (alignment/separator row is not).
        /// Row index 0 = header, 1.. = body rows.
        /// </summary>
        private static TextSpan[,] BuildCellSpans(MarkdownDocument document, TableModel table)
        {
            int rows = table.RowCount;
            int cols = table.ColumnCount;

            var spans = new TextSpan[rows, cols];

            for (int r = 0; r < rows; r++)
            {
                int docLine;

                if (r == 0)
                {
                    // Header row is at StartLine
                    docLine = table.StartLine;
                }
                else
                {
                    // Body rows start at StartLine + 2 (StartLine+1 is the alignment row)
                    docLine = table.StartLine + 1 + r;
                }

                if (docLine < 0 || docLine >= document.Lines.Count)
                {
                    // Fallback: empty spans
                    for (int c = 0; c < cols; c++)
                    {
                        spans[r, c] = new TextSpan(0, 0);
                    }
                    continue;
                }

                string lineText = document.Lines[docLine] ?? string.Empty;
                var segments = ComputeCellSegmentsForLine(lineText);

                for (int c = 0; c < cols; c++)
                {
                    if (c < segments.Count)
                    {
                        var seg = segments[c];

                        int start = seg.Start;
                        int end = seg.End;

                        // Trim whitespace inside segment to get the "content" span
                        while (start < end &&
                               (lineText[start] == ' ' || lineText[start] == '\t' || lineText[start] == '|'))
                        {
                            start++;
                        }

                        while (end > start &&
                               (lineText[end - 1] == ' ' || lineText[end - 1] == '\t' || lineText[end - 1] == '|'))
                        {
                            end--;
                        }

                        int startOffset = document.GetOffset(docLine, start);
                        int endOffset = document.GetOffset(docLine, end);

                        spans[r, c] = new TextSpan(startOffset, endOffset);
                    }
                    else
                    {
                        // Missing cell: zero-length span at end of line
                        int lineLen = lineText.Length;
                        int offset = document.GetOffset(docLine, lineLen);
                        spans[r, c] = new TextSpan(offset, offset);
                    }
                }
            }

            return spans;
        }

        private readonly struct CellSegment
        {
            public readonly int Start; // inclusive
            public readonly int End;   // exclusive

            public CellSegment(int start, int end)
            {
                Start = start;
                End = end;
            }
        }

        /// <summary>
        /// Compute raw cell segments for a table line based on '|' positions.
        /// We don't validate column count here; the caller will clamp.
        /// </summary>
        private static List<CellSegment> ComputeCellSegmentsForLine(string line)
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
                // No pipes: treat whole line as a single segment
                segments.Add(new CellSegment(0, len));
                return segments;
            }

            // Detect leading pipe after optional whitespace
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
                        segments.Add(new CellSegment(start, end));
                }

                // Trailing segment if there is content after the last '|'
                int lastBar = bars[bars.Count - 1];
                if (lastBar < len - 1)
                {
                    segments.Add(new CellSegment(lastBar + 1, len));
                }
            }
            else
            {
                // Example: "a | b | c" (no leading pipe)
                // First segment: from start to first pipe
                segments.Add(new CellSegment(0, bars[0]));

                // Middle segments: between pipes
                for (int i = 0; i < bars.Count - 1; i++)
                {
                    int start = bars[i] + 1;
                    int end = bars[i + 1];

                    if (end > start)
                        segments.Add(new CellSegment(start, end));
                }

                // Trailing segment if there is content after last pipe
                int lastBar = bars[bars.Count - 1];
                if (lastBar < len - 1)
                {
                    segments.Add(new CellSegment(lastBar + 1, len));
                }
            }

            return segments;
        }
    }
}
