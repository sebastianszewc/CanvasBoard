using System;
using System.Collections.Generic;
using System.Text;

namespace CanvasBoard.App.Markdown.Document
{
    public sealed class MarkdownDocument
    {
        public List<string> Lines { get; } = new();

        public int CaretLine { get; private set; }
        public int CaretColumn { get; private set; }

        public MarkdownDocument()
        {
            Lines.Add(string.Empty);
            CaretLine = 0;
            CaretColumn = 0;
        }

        public void SetText(string text)
        {
            Lines.Clear();

            if (string.IsNullOrEmpty(text))
            {
                Lines.Add(string.Empty);
            }
            else
            {
                var normalized = text.Replace("\r\n", "\n");
                var split = normalized.Split('\n');
                Lines.AddRange(split);
            }

            CaretLine = Math.Clamp(CaretLine, 0, Lines.Count - 1);
            CaretColumn = Math.Clamp(CaretColumn, 0, Lines[CaretLine].Length);
        }

        public string GetText()
        {
            // Use Environment.NewLine for external representation
            return string.Join(Environment.NewLine, Lines);
        }

        public void SetCaret(int line, int column)
        {
            line = Math.Clamp(line, 0, Lines.Count - 1);
            column = Math.Clamp(column, 0, Lines[line].Length);

            CaretLine = line;
            CaretColumn = column;
        }

        public void MoveCaretToLineStart()
        {
            CaretColumn = 0;
        }

        public void MoveCaretToLineEnd()
        {
            CaretColumn = Lines[CaretLine].Length;
        }

        public void MoveCaretLeft()
        {
            if (CaretColumn > 0)
            {
                CaretColumn--;
                return;
            }

            if (CaretLine > 0)
            {
                CaretLine--;
                CaretColumn = Lines[CaretLine].Length;
            }
        }

        public void MoveCaretRight()
        {
            var line = Lines[CaretLine];
            if (CaretColumn < line.Length)
            {
                CaretColumn++;
                return;
            }

            if (CaretLine + 1 < Lines.Count)
            {
                CaretLine++;
                CaretColumn = 0;
            }
        }

        public void MoveCaretUp()
        {
            if (CaretLine == 0)
                return;

            int target = CaretLine - 1;
            CaretLine = target;
            CaretColumn = Math.Clamp(CaretColumn, 0, Lines[target].Length);
        }

        public void MoveCaretDown()
        {
            if (CaretLine + 1 >= Lines.Count)
                return;

            int target = CaretLine + 1;
            CaretLine = target;
            CaretColumn = Math.Clamp(CaretColumn, 0, Lines[target].Length);
        }

        /// <summary>
        /// Ctrl+Left: move caret to the start of the previous word.
        /// </summary>
        public void MoveCaretWordLeft()
        {
            var line = Lines[CaretLine] ?? string.Empty;

            // If at start of line, go to end of previous line if any.
            if (CaretColumn <= 0)
            {
                if (CaretLine > 0)
                {
                    CaretLine--;
                    CaretColumn = (Lines[CaretLine] ?? string.Empty).Length;
                }
                return;
            }

            int index = CaretColumn - 1;

            // Skip any whitespace directly to the left
            while (index >= 0 && char.IsWhiteSpace(line[index]))
                index--;

            // Skip non-whitespace (the word itself)
            while (index >= 0 && !char.IsWhiteSpace(line[index]))
                index--;

            CaretColumn = index + 1;
        }

        /// <summary>
        /// Ctrl+Right: move caret to the start of the next word.
        /// </summary>
        public void MoveCaretWordRight()
        {
            var line = Lines[CaretLine] ?? string.Empty;
            int length = line.Length;

            // If at end of line, move to start of next line if any.
            if (CaretColumn >= length)
            {
                if (CaretLine + 1 < Lines.Count)
                {
                    CaretLine++;
                    CaretColumn = 0;
                }
                return;
            }

            int index = CaretColumn;

            // If currently in a word, skip to end of this word
            if (!char.IsWhiteSpace(line[index]))
            {
                while (index < length && !char.IsWhiteSpace(line[index]))
                    index++;
            }

            // Then skip whitespace to start of next word
            while (index < length && char.IsWhiteSpace(line[index]))
                index++;

            CaretColumn = index;
        }

        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // We keep InsertNewLine() as separate; here we ignore any '\n' in text.
            text = text.Replace("\r", "").Replace("\n", "");

            var line = Lines[CaretLine];
            if (CaretColumn < 0 || CaretColumn > line.Length)
                CaretColumn = Math.Clamp(CaretColumn, 0, line.Length);

            line = line.Insert(CaretColumn, text);
            Lines[CaretLine] = line;
            CaretColumn += text.Length;
        }

        /// <summary>
        /// Insert text that may contain newlines at the current caret position.
        /// </summary>
        public void InsertTextWithNewlines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var parts = text.Split('\n');

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    InsertNewLine();

                if (parts[i].Length > 0)
                    InsertText(parts[i]);
            }
        }

        public void InsertNewLine()
        {
            var line = Lines[CaretLine];
            string left = line[..CaretColumn];
            string right = line[ CaretColumn.. ];

            Lines[CaretLine] = left;
            Lines.Insert(CaretLine + 1, right);

            CaretLine++;
            CaretColumn = 0;
        }

        public void Backspace()
        {
            if (CaretColumn > 0)
            {
                var line = Lines[CaretLine];
                line = line.Remove(CaretColumn - 1, 1);
                Lines[CaretLine] = line;
                CaretColumn--;
                return;
            }

            if (CaretLine == 0)
                return;

            // Merge with previous line
            var prevLine = Lines[CaretLine - 1];
            var currLine = Lines[CaretLine];

            int newCol = prevLine.Length;
            Lines[CaretLine - 1] = prevLine + currLine;
            Lines.RemoveAt(CaretLine);

            CaretLine--;
            CaretColumn = newCol;
        }

        public void Delete()
        {
            var line = Lines[CaretLine];

            if (CaretColumn < line.Length)
            {
                line = line.Remove(CaretColumn, 1);
                Lines[CaretLine] = line;
                return;
            }

            if (CaretLine + 1 >= Lines.Count)
                return;

            // Merge with next line
            var nextLine = Lines[CaretLine + 1];
            Lines[CaretLine] = line + nextLine;
            Lines.RemoveAt(CaretLine + 1);
        }

        public string GetText(LineSpan span)
        {
            int sl = span.StartLine;
            int sc = span.StartColumn;
            int el = span.EndLine;
            int ec = span.EndColumn;

            sl = Math.Clamp(sl, 0, Lines.Count - 1);
            el = Math.Clamp(el, 0, Lines.Count - 1);

            if (sl > el)
                (sl, el) = (el, sl);

            var sb = new StringBuilder();

            if (sl == el)
            {
                var line = Lines[sl] ?? string.Empty;
                sc = Math.Clamp(sc, 0, line.Length);
                ec = Math.Clamp(ec, 0, line.Length);
                if (ec > sc)
                    sb.Append(line.Substring(sc, ec - sc));

                return sb.ToString();
            }

            // First line: from sc to end
            var firstLine = Lines[sl] ?? string.Empty;
            sc = Math.Clamp(sc, 0, firstLine.Length);
            sb.Append(firstLine.Substring(sc));
            sb.Append(Environment.NewLine);

            // Middle lines: full
            for (int i = sl + 1; i < el; i++)
            {
                sb.Append(Lines[i] ?? string.Empty);
                sb.Append(Environment.NewLine);
            }

            // Last line: from 0 to ec
            var lastLine = Lines[el] ?? string.Empty;
            ec = Math.Clamp(ec, 0, lastLine.Length);
            if (ec > 0)
                sb.Append(lastLine.Substring(0, ec));

            return sb.ToString();
        }

        public void DeleteSpan(LineSpan span)
        {
            int sl = span.StartLine;
            int sc = span.StartColumn;
            int el = span.EndLine;
            int ec = span.EndColumn;

            sl = Math.Clamp(sl, 0, Lines.Count - 1);
            el = Math.Clamp(el, 0, Lines.Count - 1);

            if (sl > el)
                (sl, el) = (el, sl);

            if (sl == el)
            {
                var line = Lines[sl] ?? string.Empty;
                sc = Math.Clamp(sc, 0, line.Length);
                ec = Math.Clamp(ec, 0, line.Length);
                int len = ec - sc;
                if (len <= 0)
                    return;

                Lines[sl] = line.Remove(sc, len);
                return;
            }

            var firstLine = Lines[sl] ?? string.Empty;
            var lastLine = Lines[el] ?? string.Empty;

            sc = Math.Clamp(sc, 0, firstLine.Length);
            ec = Math.Clamp(ec, 0, lastLine.Length);

            string newFirst = firstLine.Substring(0, sc) + lastLine.Substring(ec);
            Lines[sl] = newFirst;

            // Remove intermediate and last lines
            for (int i = el; i > sl; i--)
            {
                Lines.RemoveAt(i);
            }
        }

        /// <summary>
        /// Replace the text in the given span with newText and return the old text.
        /// Also returns the resulting span of the inserted text.
        /// </summary>
        public string ReplaceRange(LineSpan span, string newText, out LineSpan resultingRange)
        {
            int sl = span.StartLine;
            int sc = span.StartColumn;
            int el = span.EndLine;
            int ec = span.EndColumn;

            sl = Math.Clamp(sl, 0, Lines.Count - 1);
            el = Math.Clamp(el, 0, Lines.Count - 1);

            if (sl > el || (sl == el && sc > ec))
            {
                (sl, el) = (el, sl);
                (sc, ec) = (ec, sc);
            }

            var normalized = new LineSpan(sl, sc, el, ec);

            string oldText = GetText(normalized);

            DeleteSpan(normalized);
            SetCaret(normalized.StartLine, normalized.StartColumn);

            if (!string.IsNullOrEmpty(newText))
            {
                InsertTextWithNewlines(newText);
            }

            resultingRange = new LineSpan(
                normalized.StartLine,
                normalized.StartColumn,
                CaretLine,
                CaretColumn);

            return oldText;
        }
        public void SetCaretPosition(int line, int column)
        {
            // Clamp defensively if you want, or just assign.
            CaretLine = line;
            CaretColumn = column;
        }    
        public int GetOffset(int line, int column)
        {
            if (Lines.Count == 0)
                return 0;

            line = Math.Clamp(line, 0, Lines.Count - 1);
            var lineText = Lines[line] ?? string.Empty;
            column = Math.Clamp(column, 0, lineText.Length);

            int offset = 0;

            for (int i = 0; i < line; i++)
            {
                var lt = Lines[i] ?? string.Empty;
                offset += lt.Length;
                // newline separator between lines
                offset += 1;
            }

            offset += column;
            return offset;
        }
        public (int line, int column) GetLineColumn(int offset)
        {
            if (Lines.Count == 0)
                return (0, 0);

            if (offset <= 0)
                return (0, 0);

            int total = 0;

            for (int i = 0; i < Lines.Count; i++)
            {
                var lt = Lines[i] ?? string.Empty;
                int lineLen = lt.Length;
                int lineStart = total;
                int lineEnd = lineStart + lineLen; // exclusive

                if (offset <= lineEnd)
                {
                    int column = offset - lineStart;
                    column = Math.Clamp(column, 0, lineLen);
                    return (i, column);
                }

                // move past this line and its newline separator
                total = lineEnd + 1;
            }

            // Beyond the end: clamp to end of last line
            int lastIndex = Lines.Count - 1;
            var lastText = Lines[lastIndex] ?? string.Empty;
            return (lastIndex, lastText.Length);
        }    
    }
}
