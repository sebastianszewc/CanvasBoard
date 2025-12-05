using System;
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

        // --------------------
        // Indent / Outdent with real tabs
        // --------------------

        private void IndentLines(int fromLine, int toLine)
        {
            if (fromLine > toLine)
                (fromLine, toLine) = (toLine, fromLine);

            for (int line = fromLine; line <= toLine; line++)
            {
                if (line < 0 || line >= Document.Lines.Count)
                    continue;

                string old = Document.Lines[line] ?? string.Empty;
                Document.Lines[line] = "\t" + old;
            }

            // Adjust caret and selection anchor by one character to the right
            if (Document.CaretLine >= fromLine && Document.CaretLine <= toLine)
                Document.SetCaret(Document.CaretLine, Document.CaretColumn + 1);

            if (_selectionAnchorLine >= fromLine && _selectionAnchorLine <= toLine)
                _selectionAnchorColumn += 1;
        }

        private void OutdentLines(int fromLine, int toLine)
        {
            if (fromLine > toLine)
                (fromLine, toLine) = (toLine, fromLine);

            for (int line = fromLine; line <= toLine; line++)
            {
                if (line < 0 || line >= Document.Lines.Count)
                    continue;

                string old = Document.Lines[line] ?? string.Empty;

                if (old.StartsWith("\t"))
                {
                    Document.Lines[line] = old.Substring(1);
                }
                else
                {
                    // Fallback: remove up to TabSize leading spaces
                    int remove = 0;
                    while (remove < TabSize && remove < old.Length && old[remove] == ' ')
                        remove++;

                    if (remove > 0)
                        Document.Lines[line] = old.Substring(remove);
                }
            }

            // Rough caret / anchor adjustment: shift left by one character
            if (Document.CaretLine >= fromLine && Document.CaretLine <= toLine)
            {
                int newCol = Math.Max(0, Document.CaretColumn - 1);
                Document.SetCaret(Document.CaretLine, newCol);
            }

            if (_selectionAnchorLine >= fromLine && _selectionAnchorLine <= toLine)
            {
                _selectionAnchorColumn = Math.Max(0, _selectionAnchorColumn - 1);
            }
        }
    }
}
