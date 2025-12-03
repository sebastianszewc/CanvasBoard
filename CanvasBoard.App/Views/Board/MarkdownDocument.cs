using System;
using System.Collections.Generic;
using System.Text;

namespace CanvasBoard.App.Views.Board;

public sealed class MarkdownDocument
{
    public List<string> Lines { get; } = new() { string.Empty };

    public int CaretLine { get; private set; }
    public int CaretColumn { get; private set; }

    public string GetText()
    {
        return string.Join("\n", Lines);
    }

    public void SetText(string text)
    {
        Lines.Clear();
        if (text == null)
        {
            Lines.Add(string.Empty);
        }
        else
        {
            var split = text.Replace("\r\n", "\n").Split('\n');
            Lines.AddRange(split);
            if (Lines.Count == 0)
                Lines.Add(string.Empty);
        }

        CaretLine = Math.Clamp(CaretLine, 0, Lines.Count - 1);
        CaretColumn = Math.Clamp(CaretColumn, 0, Lines[CaretLine].Length);
    }

    private void ClampCaret()
    {
        CaretLine = Math.Clamp(CaretLine, 0, Lines.Count - 1);
        CaretColumn = Math.Clamp(CaretColumn, 0, Lines[CaretLine].Length);
    }

    public void MoveCaretLeft()
    {
        if (CaretColumn > 0)
        {
            CaretColumn--;
        }
        else if (CaretLine > 0)
        {
            CaretLine--;
            CaretColumn = Lines[CaretLine].Length;
        }
    }

    public void MoveCaretRight()
    {
        if (CaretColumn < Lines[CaretLine].Length)
        {
            CaretColumn++;
        }
        else if (CaretLine < Lines.Count - 1)
        {
            CaretLine++;
            CaretColumn = 0;
        }
    }

    public void MoveCaretUp()
    {
        if (CaretLine > 0)
        {
            CaretLine--;
            CaretColumn = Math.Clamp(CaretColumn, 0, Lines[CaretLine].Length);
        }
    }

    public void MoveCaretDown()
    {
        if (CaretLine < Lines.Count - 1)
        {
            CaretLine++;
            CaretColumn = Math.Clamp(CaretColumn, 0, Lines[CaretLine].Length);
        }
    }

    public void MoveCaretToLineStart()
    {
        CaretColumn = 0;
    }

    public void MoveCaretToLineEnd()
    {
        CaretColumn = Lines[CaretLine].Length;
    }

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var ch in text)
        {
            if (ch == '\r')
                continue;

            if (ch == '\n')
            {
                InsertNewLine();
            }
            else
            {
                InsertChar(ch);
            }
        }
    }

    public void InsertChar(char ch)
    {
        var line = Lines[CaretLine];
        if (CaretColumn < 0 || CaretColumn > line.Length)
            CaretColumn = line.Length;

        Lines[CaretLine] = line.Insert(CaretColumn, ch.ToString());
        CaretColumn++;
    }

    public void InsertNewLine()
    {
        var line = Lines[CaretLine];
        var before = line.Substring(0, CaretColumn);
        var after = line.Substring(CaretColumn);

        Lines[CaretLine] = before;
        Lines.Insert(CaretLine + 1, after);

        CaretLine++;
        CaretColumn = 0;
    }

    public void Backspace()
    {
        if (CaretColumn > 0)
        {
            var line = Lines[CaretLine];
            Lines[CaretLine] = line.Remove(CaretColumn - 1, 1);
            CaretColumn--;
        }
        else if (CaretLine > 0)
        {
            // merge with previous line
            var current = Lines[CaretLine];
            CaretLine--;
            CaretColumn = Lines[CaretLine].Length;
            Lines[CaretLine] = Lines[CaretLine] + current;
            Lines.RemoveAt(CaretLine + 1);
        }
    }

    public void Delete()
    {
        var line = Lines[CaretLine];

        if (CaretColumn < line.Length)
        {
            Lines[CaretLine] = line.Remove(CaretColumn, 1);
        }
        else if (CaretLine < Lines.Count - 1)
        {
            // merge with next line
            var next = Lines[CaretLine + 1];
            Lines[CaretLine] = line + next;
            Lines.RemoveAt(CaretLine + 1);
        }
    }

    public void SetCaret(int line, int column)
    {
        CaretLine = Math.Clamp(line, 0, Lines.Count - 1);
        CaretColumn = Math.Clamp(column, 0, Lines[CaretLine].Length);
    }
}
