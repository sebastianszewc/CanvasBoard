using System;
using System.Collections.Generic;
using Avalonia.Media;
using CanvasBoard.App.Markdown.Document;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl
    {
        [Flags]
        private enum InlineStyle
        {
            None    = 0,
            Emph    = 1 << 0,  // *text* or _text_
            Strong  = 1 << 1,  // **text** or __text__
            Code    = 1 << 2,  // `code`
            Link    = 1 << 3,  // [text](url)
            Strike  = 1 << 4   // ~~text~~
        }

        private struct InlineRun
        {
            public int StartColumn;
            public int Length;
            public InlineStyle Styles;
        }

        // Per-line inline runs
        private readonly List<List<InlineRun>> _inlineRunsByLine = new();

        private void EnsureInlineStyles()
        {
            _inlineRunsByLine.Clear();

            for (int i = 0; i < Document.Lines.Count; i++)
            {
                string line = Document.Lines[i] ?? string.Empty;
                _inlineRunsByLine.Add(ParseInlineRuns(line));
            }
        }

        private sealed class StyleRange
        {
            public int Start;
            public int End; // exclusive
            public InlineStyle Style;
        }

        private List<InlineRun> ParseInlineRuns(string line)
        {
            var result = new List<InlineRun>();

            if (string.IsNullOrEmpty(line))
            {
                result.Add(new InlineRun { StartColumn = 0, Length = 0, Styles = InlineStyle.None });
                return result;
            }

            int len = line.Length;
            var styles = new List<StyleRange>();

            // Stacks for emphasis/strong/strike
            var emphStack = new Stack<(char marker, int pos)>();
            var strongStack = new Stack<(char marker, int pos)>();
            var strikeStack = new Stack<int>();

            int i = 0;
            while (i < len)
            {
                char c = line[i];

                // Code span: `code`
                if (c == '`')
                {
                    int closing = line.IndexOf('`', i + 1);
                    if (closing > i + 1)
                    {
                        styles.Add(new StyleRange
                        {
                            Start = i,
                            End = closing + 1,
                            Style = InlineStyle.Code
                        });
                        i = closing + 1;
                        continue;
                    }
                }

                // Link: [text](url)
                if (c == '[')
                {
                    int closeBracket = line.IndexOf(']', i + 1);
                    if (closeBracket > i + 1 && closeBracket + 1 < len && line[closeBracket + 1] == '(')
                    {
                        int closeParen = line.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket + 2)
                        {
                            styles.Add(new StyleRange
                            {
                                Start = i,
                                End = closeParen + 1,
                                Style = InlineStyle.Link
                            });
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }

                // Strike: ~~text~~
                if (c == '~' && i + 1 < len && line[i + 1] == '~')
                {
                    if (strikeStack.Count == 0)
                    {
                        strikeStack.Push(i);
                    }
                    else
                    {
                        int open = strikeStack.Pop();
                        int end = i + 2;
                        if (end > open + 2)
                        {
                            styles.Add(new StyleRange
                            {
                                Start = open,
                                End = end,
                                Style = InlineStyle.Strike
                            });
                        }
                    }

                    i += 2;
                    continue;
                }

                // Strong / Emphasis: *, _
                if (c == '*' || c == '_')
                {
                    bool isDouble = (i + 1 < len && line[i + 1] == c);

                    if (isDouble)
                    {
                        // Strong: **text** or __text__
                        if (strongStack.Count == 0 || strongStack.Peek().marker != c)
                        {
                            strongStack.Push((c, i));
                        }
                        else
                        {
                            var open = strongStack.Pop();
                            int end = i + 2;
                            if (end > open.pos + 2)
                            {
                                styles.Add(new StyleRange
                                {
                                    Start = open.pos,
                                    End = end,
                                    Style = InlineStyle.Strong
                                });
                            }
                        }

                        i += 2;
                        continue;
                    }
                    else
                    {
                        // Emphasis: *text* or _text_
                        if (emphStack.Count == 0 || emphStack.Peek().marker != c)
                        {
                            emphStack.Push((c, i));
                        }
                        else
                        {
                            var open = emphStack.Pop();
                            int end = i + 1;
                            if (end > open.pos + 1)
                            {
                                styles.Add(new StyleRange
                                {
                                    Start = open.pos,
                                    End = end,
                                    Style = InlineStyle.Emph
                                });
                            }
                        }

                        i += 1;
                        continue;
                    }
                }

                i++;
            }

            // Now convert style ranges into InlineRuns by merging flags per character

            if (styles.Count == 0)
            {
                result.Add(new InlineRun
                {
                    StartColumn = 0,
                    Length = len,
                    Styles = InlineStyle.None
                });
                return result;
            }

            // For each character, compute accumulated style flags
            var styleAtPos = new InlineStyle[len];

            foreach (var r in styles)
            {
                int start = System.Math.Clamp(r.Start, 0, len);
                int end = System.Math.Clamp(r.End, start, len);

                for (int p = start; p < end; p++)
                {
                    styleAtPos[p] |= r.Style;
                }
            }

            // Convert styleAtPos[] into runs
            int runStart = 0;
            InlineStyle current = styleAtPos[0];

            for (int p = 1; p < len; p++)
            {
                if (styleAtPos[p] != current)
                {
                    int runLen = p - runStart;
                    if (runLen > 0)
                    {
                        result.Add(new InlineRun
                        {
                            StartColumn = runStart,
                            Length = runLen,
                            Styles = current
                        });
                    }

                    runStart = p;
                    current = styleAtPos[p];
                }
            }

            // Last run
            int finalLen = len - runStart;
            if (finalLen > 0)
            {
                result.Add(new InlineRun
                {
                    StartColumn = runStart,
                    Length = finalLen,
                    Styles = current
                });
            }

            return result;
        }

        private List<InlineRun> GetInlineRunsForLine(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _inlineRunsByLine.Count)
                return null;

            return _inlineRunsByLine[lineIndex];
        }

        private InlineStyle GetInlineStyleAtColumn(int lineIndex, int column)
        {
            var runs = GetInlineRunsForLine(lineIndex);
            if (runs == null)
                return InlineStyle.None;

            foreach (var r in runs)
            {
                int start = r.StartColumn;
                int end = start + r.Length;
                if (column >= start && column < end)
                    return r.Styles;
            }

            return InlineStyle.None;
        }

        private (Typeface typeface, IBrush brush) GetStyleForInline(
            MarkdownLineKind lineKind,
            InlineStyle styleFlags)
        {
            // Base line style
            var baseTypeface = GetTypefaceForLineKind(lineKind);
            var baseBrush = GetBrushForLineKind(lineKind);

            // Code overrides most things visually
            if ((styleFlags & InlineStyle.Code) != 0)
            {
                return (new Typeface(baseTypeface.FontFamily, FontStyle.Normal, FontWeight.Normal), _codeBrush);
            }

            // Link color
            var brush = baseBrush;
            if ((styleFlags & InlineStyle.Link) != 0)
            {
                brush = new SolidColorBrush(Color.FromRgb(0x66, 0xAA, 0xFF)); // link blue
            }

            if ((styleFlags & InlineStyle.Strike) != 0)
            {
                brush = new SolidColorBrush(Color.FromRgb(0xCC, 0x88, 0x88));
            }

            // Weight / italic from Emph + Strong
            FontStyle fs = baseTypeface.Style;
            FontWeight fw = baseTypeface.Weight;

            if ((styleFlags & InlineStyle.Emph) != 0)
                fs = FontStyle.Italic;

            if ((styleFlags & InlineStyle.Strong) != 0)
                fw = FontWeight.Bold;

            var tf = new Typeface(baseTypeface.FontFamily, fs, fw);
            return (tf, brush);
        }
    }
}
