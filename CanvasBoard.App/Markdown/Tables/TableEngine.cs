using System.Collections.Generic;
using CanvasBoard.App.Markdown.Document;

namespace CanvasBoard.App.Markdown.Tables
{
    public sealed class TableEngine
    {
        private readonly List<TableModel> _tables;

        public IReadOnlyList<TableModel> Tables => _tables;

        public TableEngine(MarkdownDocument document)
        {
            _tables = TableParser.Parse(document.Lines);
        }

        public TableModel? FindTableAtLine(int lineIndex)
        {
            foreach (var t in _tables)
            {
                if (lineIndex >= t.StartLine && lineIndex <= t.EndLine)
                    return t;
            }

            return null;
        }
    }
}
