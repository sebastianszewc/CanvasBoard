using System;
using System.Collections.Generic;
using CanvasBoard.App.Markdown.Document;
using CanvasBoard.App.Markdown.Model;
using CanvasBoard.App.Markdown.Tables;

namespace CanvasBoard.App.Markdown.Syntax
{
    /// <summary>
    /// Builds a high-level block model (MarkdownDocumentModel) from the
    /// underlying MarkdownDocument text.
    ///
    /// Currently supports:
    /// - Paragraph blocks
    /// - Heading blocks (# .. ######)
    /// - Horizontal rules (---, *** , ___)
    /// - TableBlock (using TableParser + per-cell spans)
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
                // TABLE
                if (tableIdx < tables.Count &&
                    line == tables[tableIdx].StartLine)
                {
                    var t = tables[tableIdx];

                    int startLine = t.StartLine;
                    int endLine = t.EndLine;

                    int startOffset = document.GetOffset(startLine, 0);
                    string lastLineText = lines[endLine] ?? string.Empty;
                    int endOffset = document.GetOffset(endLine, lastLineText.Length);

                    var span = new TextSpan(startOffset, endOffset);

                    var cellSpans = BuildCellSpans(document, t);

                    var tableBlock = new TableBlock(
                        span,
                        startLine,
                        endLine,
                        t,
                        cellSpans);

                    blocks.Add(tableBlock);

                    line = endLine + 1;
                    tableIdx++;
                    continue;
                }

                // NON-TABLE REGION: check for headings, HR, then paragraphs

                string current = lines[line] ?? string.Empty;

                // Skip pure blank lines (no block)
                if (string.IsNullOrWhiteSpace(current))
                {
                    line++;
                    continue;
                }

                // HEADING?
                if (TryParseAtxHeading(current, out int level))
                {
                    int startOffset = document.GetOffset(line, 0);
                    int endOffset = document.GetOffset(line, current.Length);
                    var span = new TextSpan(startOffset, endOffset);

                    var heading = new HeadingBlock(
                        span,
                        line,
                        line,
                        level);

                    blocks.Add(heading);
                    line++;
                    continue;
                }

                // HORIZONTAL RULE?
                if (IsHorizontalRuleLine(current))
                {
                    int startOffset = document.GetOffset(line, 0);
                    int endOffset = document.GetOffset(line, current.Length);
                    var span = new TextSpan(startOffset, endOffset);

                    var hr = new SimpleBlock(
                        MarkdownBlockKind.HorizontalRule,
                        span,
                        line,
                        line);

                    blocks.Add(hr);
                    line++;
                    continue;
                }

                // PARAGRAPH: collect consecutive non-table, non-blank, non-heading, non-HR lines
                int paraStartLine = line;

                while (line < lineCount)
                {
                    if (tableIdx < tables.Count &&
                        line == tables[tableIdx].StartLine)
                    {
                        break; // next table starts
                    }

                    string ltext = lines[line] ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(ltext))
                        break; // blank = paragraph boundary

                    if (TryParseAtxHeading(ltext, out _) || IsHorizontalRuleLine(ltext))
                        break; // next heading / HR starts new block

                    line++;
                }

                int paraEndLine = line - 1;

                if (paraEndLine >= paraStartLine)
                {
                    int startOffset = document.GetOffset(paraStartLine, 0);
                    string lastLineText2 = lines[paraEndLine] ?? string.Empty;
                    int endOffset = document.GetOffset(paraEndLine, lastLineText2.Length);

                    var span = new TextSpan(startOffset, endOffset);

                    var para = new SimpleBlock(
                        MarkdownBlockKind.Paragraph,
                        span,
                        paraStartLine,
                        paraEndLine);

                    blocks.Add(para);
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
                    for (int c = 0; c < cols; c++)
                        spans[r, c] = new TextSpan(0, 0);
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

        private static List<CellSegment> ComputeCellSegmentsForLine(string line)
        {
            var segments = new List<CellSegment>();

            if (string.IsNullOrEmpty(line))
                return segments;

            int len = line.Length;

            var bars = new List<int>();
            for (int i = 0; i < len; i++)
            {
                if (line[i] == '|')
                    bars.Add(i);
            }

            if (bars.Count == 0)
            {
                segments.Add(new CellSegment(0, len));
                return segments;
            }

            int firstNonWs = 0;
            while (firstNonWs < len && (line[firstNonWs] == ' ' || line[firstNonWs] == '\t'))
                firstNonWs++;

            bool hasLeadingPipe = firstNonWs < len && line[firstNonWs] == '|';

            if (hasLeadingPipe)
            {
                for (int i = 0; i < bars.Count - 1; i++)
                {
                    int start = bars[i] + 1;
                    int end = bars[i + 1];

                    if (end > start)
                        segments.Add(new CellSegment(start, end));
                }

                int lastBar = bars[bars.Count - 1];
                if (lastBar < len - 1)
                {
                    segments.Add(new CellSegment(lastBar + 1, len));
                }
            }
            else
            {
                segments.Add(new CellSegment(0, bars[0]));

                for (int i = 0; i < bars.Count - 1; i++)
                {
                    int start = bars[i] + 1;
                    int end = bars[i + 1];

                    if (end > start)
                        segments.Add(new CellSegment(start, end));
                }

                int lastBar = bars[bars.Count - 1];
                if (lastBar < len - 1)
                {
                    segments.Add(new CellSegment(lastBar + 1, len));
                }
            }

            return segments;
        }

        private static bool TryParseAtxHeading(string line, out int level)
        {
            level = 0;
            if (string.IsNullOrEmpty(line))
                return false;

            int i = 0;
            while (i < line.Length && line[i] == '#')
                i++;

            if (i == 0 || i > 6)
                return false;

            // Must be followed by space or end-of-line to be a heading
            if (i < line.Length && !char.IsWhiteSpace(line[i]))
                return false;

            level = i;
            return true;
        }

        private static bool IsHorizontalRuleLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return false;

            var trimmed = rawLine.Trim();

            if (trimmed.Length < 3)
                return false;

            char c = trimmed[0];
            if (c != '-' && c != '*' && c != '_')
                return false;

            for (int i = 1; i < trimmed.Length; i++)
            {
                if (trimmed[i] != c)
                    return false;
            }

            return true;
        }
    }
}
