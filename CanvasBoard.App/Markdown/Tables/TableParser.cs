using System;
using System.Collections.Generic;

namespace CanvasBoard.App.Markdown.Tables
{
    public static class TableParser
    {
        /// <summary>
        /// Parse all markdown tables in the given document lines.
        /// Tables follow GitHub-style pipe table syntax.
        /// </summary>
        public static List<TableModel> Parse(IReadOnlyList<string> lines)
        {
            var result = new List<TableModel>();

            if (lines == null || lines.Count == 0)
                return result;

            int i = 0;
            while (i < lines.Count - 1)
            {
                // Header candidate
                var headerCells = TryParseRow(lines[i]);
                if (headerCells == null || headerCells.Length == 0)
                {
                    i++;
                    continue;
                }

                // Alignment row
                var alignments = TryParseAlignmentRow(lines[i + 1], headerCells.Length);
                if (alignments == null)
                {
                    i++;
                    continue;
                }

                // We have a table starting at i, alignment at i+1
                var rows = new List<string[]>();
                rows.Add(headerCells);

                int lineIndex = i + 2;
                while (lineIndex < lines.Count)
                {
                    var rowCells = TryParseRow(lines[lineIndex]);
                    if (rowCells == null || rowCells.Length != headerCells.Length)
                        break;

                    rows.Add(rowCells);
                    lineIndex++;
                }

                int startLine = i;
                int endLine = lineIndex - 1;

                var table = new TableModel(startLine, endLine, alignments, rows);
                result.Add(table);

                // Continue scanning after this table
                i = lineIndex;
            }

            return result;
        }

        /// <summary>
        /// Parse a single table row line into cells.
        /// Returns null if it doesn't look like a table row at all.
        /// </summary>
        private static string[]? TryParseRow(string line)
        {
            if (line == null)
                return null;

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                return null;

            // Must contain at least one pipe to be a table row
            if (!trimmed.Contains("|"))
                return null;

            // Remove a single leading/trailing pipe if present
            if (trimmed.StartsWith("|"))
                trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|"))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            var rawCells = trimmed.Split('|');
            var cells = new List<string>(rawCells.Length);

            foreach (var c in rawCells)
            {
                cells.Add(c.Trim());
            }

            // Require at least 2 cells to treat it as a table row
            if (cells.Count < 2)
                return null;

            return cells.ToArray();
        }

        /// <summary>
        /// Try parse the alignment row (the one with --- / :--- / ---: / :---:).
        /// </summary>
        private static TableAlignment[]? TryParseAlignmentRow(string line, int expectedColumns)
        {
            var cells = TryParseRow(line);
            if (cells == null || cells.Length != expectedColumns)
                return null;

            var alignments = new TableAlignment[cells.Length];

            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                var align = ParseAlignmentCell(c);
                if (align == null)
                    return null;

                alignments[i] = align.Value;
            }

            return alignments;
        }

        /// <summary>
        /// Parse a single alignment cell like "---", ":---", "---:", ":---:" into a TableAlignment.
        /// Returns null if the cell is not a valid alignment spec.
        /// </summary>
        private static TableAlignment? ParseAlignmentCell(string cell)
        {
            if (cell == null)
                return null;

            var s = cell.Trim();
            if (s.Length == 0)
                return null;

            bool leftColon = s.StartsWith(":", StringComparison.Ordinal);
            bool rightColon = s.EndsWith(":", StringComparison.Ordinal);

            int start = leftColon ? 1 : 0;
            int end = rightColon ? s.Length - 1 : s.Length;

            if (end <= start)
                return null;

            // Require at least one dash between colons
            bool hasDash = false;
            for (int i = start; i < end; i++)
            {
                if (s[i] != '-')
                    return null;

                hasDash = true;
            }

            if (!hasDash)
                return null;

            if (leftColon && rightColon)
                return TableAlignment.Center;
            if (leftColon)
                return TableAlignment.Left;
            if (rightColon)
                return TableAlignment.Right;

            // no colons -> default alignment
            return TableAlignment.Default;
        }
    }
}
