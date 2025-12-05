namespace CanvasBoard.App.Markdown.Document
{
    public readonly struct LineSpan
    {
        public int StartLine { get; }
        public int StartColumn { get; }
        public int EndLine   { get; }
        public int EndColumn { get; }

        public LineSpan(int startLine, int startColumn, int endLine, int endColumn)
        {
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
        }
    }
}
