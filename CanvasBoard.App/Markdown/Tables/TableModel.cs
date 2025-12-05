using System;
using System.Collections.Generic;

namespace CanvasBoard.App.Markdown.Tables
{
    public enum TableAlignment
    {
        Default,
        Left,
        Center,
        Right
    }

    public sealed class TableModel
    {
        /// <summary>Zero-based line index where the table starts (header row).</summary>
        public int StartLine { get; }

        /// <summary>Zero-based line index where the table ends (last body row).</summary>
        public int EndLine { get; }

        /// <summary>Alignment for each column (parsed from the separator row).</summary>
        public TableAlignment[] Alignments { get; }

        /// <summary>
        /// Table rows including header as the first row.
        /// Row[0] = header, Row[1..] = body rows.
        /// </summary>
        public IReadOnlyList<string[]> Rows { get; }

        public int ColumnCount => Alignments.Length;

        public int RowCount => Rows.Count;

        public string[] HeaderRow => Rows.Count > 0 ? Rows[0] : Array.Empty<string>();

        public TableModel(int startLine, int endLine, TableAlignment[] alignments, List<string[]> rows)
        {
            if (alignments == null) throw new ArgumentNullException(nameof(alignments));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0) throw new ArgumentException("Table must have at least one row.", nameof(rows));

            StartLine = startLine;
            EndLine = endLine;
            Alignments = alignments;
            Rows = rows;
        }

        public string GetCell(int row, int column)
        {
            if (row < 0 || row >= Rows.Count)
                return string.Empty;
            var r = Rows[row];
            if (column < 0 || column >= r.Length)
                return string.Empty;
            return r[column];
        }
    }
}
