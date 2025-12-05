using System;
using CanvasBoard.App.Markdown.Tables;

namespace CanvasBoard.App.Markdown.Model
{
    public enum MarkdownBlockKind
    {
        Paragraph,
        Heading,
        List,
        ListItem,
        BlockQuote,
        CodeFence,
        CodeBlock,
        Table,
        HorizontalRule
    }

    public abstract class MarkdownBlock
    {
        public MarkdownBlockKind Kind { get; }
        public TextSpan Span { get; }
        public int StartLine { get; }
        public int EndLine { get; }

        protected MarkdownBlock(
            MarkdownBlockKind kind,
            TextSpan span,
            int startLine,
            int endLine)
        {
            Kind = kind;
            Span = span;
            StartLine = startLine;
            EndLine = endLine;
        }
    }

    /// <summary>
    /// Block representing a markdown table. The backing text is still
    /// pipe-table markdown, but we track cell spans for WYSIWYG editing.
    /// </summary>
    public sealed class TableBlock : MarkdownBlock
    {
        public TableModel Table { get; }
        public TextSpan[,] CellSpans { get; }

        public int RowCount => Table.RowCount;
        public int ColumnCount => Table.ColumnCount;

        public TableBlock(
            TextSpan span,
            int startLine,
            int endLine,
            TableModel table,
            TextSpan[,] cellSpans)
            : base(MarkdownBlockKind.Table, span, startLine, endLine)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            CellSpans = cellSpans ?? throw new ArgumentNullException(nameof(cellSpans));

            if (CellSpans.GetLength(0) != table.RowCount ||
                CellSpans.GetLength(1) != table.ColumnCount)
            {
                throw new ArgumentException("CellSpans dimensions must match table size.");
            }
        }
    }
    /// <summary>
    /// ATX-style heading, e.g. "# H1", "## H2", up to "###### H6".
    /// </summary>
    public sealed class HeadingBlock : MarkdownBlock
    {
        public int Level { get; }

        public HeadingBlock(
            TextSpan span,
            int startLine,
            int endLine,
            int level)
            : base(MarkdownBlockKind.Heading, span, startLine, endLine)
        {
            Level = Math.Clamp(level, 1, 6);
        }
    }
}
