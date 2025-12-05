using System;
using System.Collections.Generic;
using CanvasBoard.App.Markdown.Document;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl
    {
        // --- Selection snapshot used by operations ---

        private struct SelectionState
        {
            public bool HasSelection;
            public int AnchorLine;
            public int AnchorColumn;
            public int CaretLine;
            public int CaretColumn;
        }

        private SelectionState CaptureSelectionState()
        {
            return new SelectionState
            {
                HasSelection = HasSelection,
                AnchorLine = _selectionAnchorLine,
                AnchorColumn = _selectionAnchorColumn,
                CaretLine = Document.CaretLine,
                CaretColumn = Document.CaretColumn
            };
        }

        private void RestoreSelectionState(in SelectionState state)
        {
            Document.SetCaret(state.CaretLine, state.CaretColumn);
            _hasSelection = state.HasSelection;
            _selectionAnchorLine = state.AnchorLine;
            _selectionAnchorColumn = state.AnchorColumn;
        }

        private static LineSpan NormalizeSpan(LineSpan span)
        {
            int sl = span.StartLine;
            int sc = span.StartColumn;
            int el = span.EndLine;
            int ec = span.EndColumn;

            if (sl > el || (sl == el && sc > ec))
            {
                (sl, el) = (el, sl);
                (sc, ec) = (ec, sc);
            }

            return new LineSpan(sl, sc, el, ec);
        }

        // --- Undoable operations ---

        private interface IUndoableOperation
        {
            void Undo(MarkdownDocument doc, MarkdownEditorControl editor);
            void Redo(MarkdownDocument doc, MarkdownEditorControl editor);
            bool TryMergeWith(IUndoableOperation next);
        }

        /// <summary>
        /// Replace a text span with new text. Almost all edits are this.
        /// </summary>
        private sealed class ReplaceRangeOperation : IUndoableOperation
        {
            private LineSpan _rangeBefore;
            private LineSpan _rangeAfter;
            private readonly string _oldText;
            private string _newText;
            private readonly SelectionState _beforeSel;
            private SelectionState _afterSel;

            private readonly bool _isPureInsertNoNewline;

            private ReplaceRangeOperation(
                LineSpan rangeBefore,
                LineSpan rangeAfter,
                string oldText,
                string newText,
                SelectionState beforeSel,
                SelectionState afterSel)
            {
                _rangeBefore = NormalizeSpan(rangeBefore);
                _rangeAfter = NormalizeSpan(rangeAfter);
                _oldText = oldText ?? string.Empty;
                _newText = newText ?? string.Empty;
                _beforeSel = beforeSel;
                _afterSel = afterSel;

                _isPureInsertNoNewline =
                    _oldText.Length == 0 &&
                    _rangeBefore.StartLine == _rangeBefore.EndLine &&
                    _rangeBefore.StartColumn == _rangeBefore.EndColumn &&
                    !_newText.Contains('\n');
            }

            public static ReplaceRangeOperation CreateAndApply(
                MarkdownDocument doc,
                MarkdownEditorControl editor,
                LineSpan rangeBefore,
                string newText,
                bool clearSelectionAfter)
            {
                var normalized = NormalizeSpan(rangeBefore);
                var beforeSel = editor.CaptureSelectionState();

                string oldText = doc.ReplaceRange(normalized, newText ?? string.Empty, out var rangeAfter);

                SelectionState afterSel;
                if (clearSelectionAfter)
                {
                    afterSel = new SelectionState
                    {
                        HasSelection = false,
                        AnchorLine = doc.CaretLine,
                        AnchorColumn = doc.CaretColumn,
                        CaretLine = doc.CaretLine,
                        CaretColumn = doc.CaretColumn
                    };

                    editor._hasSelection = false;
                    editor._selectionAnchorLine = doc.CaretLine;
                    editor._selectionAnchorColumn = doc.CaretColumn;
                }
                else
                {
                    // Keep selection as it was
                    afterSel = beforeSel;
                    editor.RestoreSelectionState(afterSel);
                }

                editor._text = doc.GetText();

                return new ReplaceRangeOperation(normalized, rangeAfter, oldText, newText ?? string.Empty, beforeSel, afterSel);
            }

            public void Undo(MarkdownDocument doc, MarkdownEditorControl editor)
            {
                // Document is in "after" form -> go back to "before"
                doc.ReplaceRange(_rangeAfter, _oldText, out var _);
                editor.RestoreSelectionState(_beforeSel);
            }

            public void Redo(MarkdownDocument doc, MarkdownEditorControl editor)
            {
                // Document is in "before" form -> apply again
                doc.ReplaceRange(_rangeBefore, _newText, out var newAfter);
                _rangeAfter = newAfter; // keep in sync
                editor.RestoreSelectionState(_afterSel);
            }

            public bool TryMergeWith(IUndoableOperation nextOp)
            {
                if (nextOp is not ReplaceRangeOperation next)
                    return false;

                // Only merge simple continuous inserts like typing
                if (!_isPureInsertNoNewline || !next._isPureInsertNoNewline)
                    return false;

                // Must be in the same "typing stream":
                // - our after selection has no selection
                // - next's before selection has no selection
                if (_afterSel.HasSelection || next._beforeSel.HasSelection)
                    return false;

                // Next insertion point must be exactly at our after caret
                if (next._rangeBefore.StartLine != _rangeAfter.EndLine ||
                    next._rangeBefore.StartColumn != _rangeAfter.EndColumn)
                    return false;

                // Merge: extend our new text and after span/state
                _newText += next._newText;

                _rangeAfter = new LineSpan(
                    _rangeBefore.StartLine,
                    _rangeBefore.StartColumn,
                    next._rangeAfter.EndLine,
                    next._rangeAfter.EndColumn);

                _afterSel = next._afterSel;

                return true;
            }

        }

        /// <summary>
        /// Full snapshot operation: used for things like indent/outdent where
        /// it's easier to treat as "replace whole text".
        /// </summary>
        private sealed class SnapshotOperation : IUndoableOperation
        {
            private readonly string _beforeText;
            private readonly string _afterText;
            private readonly SelectionState _beforeSel;
            private readonly SelectionState _afterSel;

            public SnapshotOperation(string beforeText, string afterText,
                                     SelectionState beforeSel, SelectionState afterSel)
            {
                _beforeText = beforeText ?? string.Empty;
                _afterText = afterText ?? string.Empty;
                _beforeSel = beforeSel;
                _afterSel = afterSel;
            }

            public void Undo(MarkdownDocument doc, MarkdownEditorControl editor)
            {
                doc.SetText(_beforeText);
                editor.RestoreSelectionState(_beforeSel);
            }

            public void Redo(MarkdownDocument doc, MarkdownEditorControl editor)
            {
                doc.SetText(_afterText);
                editor.RestoreSelectionState(_afterSel);
            }

            public bool TryMergeWith(IUndoableOperation next) => false;
        }

        // --- Undo manager ---

        private readonly Stack<IUndoableOperation> _undoStack = new();
        private readonly Stack<IUndoableOperation> _redoStack = new();

        private void PushOperation(IUndoableOperation op, bool allowMerge)
        {
            if (allowMerge && _undoStack.Count > 0)
            {
                var last = _undoStack.Peek();
                if (last.TryMergeWith(op))
                {
                    _redoStack.Clear();
                    return;
                }
            }

            _undoStack.Push(op);
            _redoStack.Clear();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0)
                return;

            var op = _undoStack.Pop();
            op.Undo(Document, this);
            _redoStack.Push(op);

            _text = Document.GetText();
            InvalidateVisual();
        }

        private void Redo()
        {
            if (_redoStack.Count == 0)
                return;

            var op = _redoStack.Pop();
            op.Redo(Document, this);
            _undoStack.Push(op);

            _text = Document.GetText();
            InvalidateVisual();
        }

        private void ResetUndoHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _hasSelection = false;
            _selectionAnchorLine = Document.CaretLine;
            _selectionAnchorColumn = Document.CaretColumn;
        }
    }
}
