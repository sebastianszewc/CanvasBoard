using CanvasBoard.App.Markdown.Document;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl
    {
        private bool _hasSelection;
        private int _selectionAnchorLine;
        private int _selectionAnchorColumn;

        private bool _mouseSelecting;

        private bool HasSelection =>
            _hasSelection &&
            (_selectionAnchorLine != Document.CaretLine ||
             _selectionAnchorColumn != Document.CaretColumn);

        private void ClearSelection()
        {
            _hasSelection = false;
            _selectionAnchorLine = Document.CaretLine;
            _selectionAnchorColumn = Document.CaretColumn;
        }

        private void BeginSelectionIfNeeded()
        {
            if (!_hasSelection)
            {
                _hasSelection = true;
                _selectionAnchorLine = Document.CaretLine;
                _selectionAnchorColumn = Document.CaretColumn;
            }
        }

        private bool TryGetSelectionSpan(out LineSpan span)
        {
            if (!HasSelection)
            {
                span = default;
                return false;
            }

            int al = _selectionAnchorLine;
            int ac = _selectionAnchorColumn;
            int cl = Document.CaretLine;
            int cc = Document.CaretColumn;

            // Normalize (start <= end)
            if (cl < al || (cl == al && cc < ac))
            {
                span = new LineSpan(cl, cc, al, ac);
            }
            else
            {
                span = new LineSpan(al, ac, cl, cc);
            }

            return true;
        }

        private bool DeleteSelectionIfAny()
        {
            if (!TryGetSelectionSpan(out var span))
                return false;

            Document.DeleteSpan(span);
            Document.SetCaret(span.StartLine, span.StartColumn);
            ClearSelection();
            _text = Document.GetText();
            return true;
        }
    }
}
