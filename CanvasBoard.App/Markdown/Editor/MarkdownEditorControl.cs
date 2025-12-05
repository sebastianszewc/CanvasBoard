using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CanvasBoard.App.Markdown.Document;
using CanvasBoard.App.Markdown.Tables;
using CanvasBoard.App.Markdown.Model;
using CanvasBoard.App.Markdown.Syntax;

namespace CanvasBoard.App.Views.Board
{
    public partial class MarkdownEditorControl : Control
    {
        public MarkdownDocument Document { get; } = new();

        private string _text = string.Empty;
        public string Text
        {
            get => _text;
            set
            {
                var v = value ?? string.Empty;
                if (_text == v)
                    return;

                _text = v;
                Document.SetText(_text);
                ResetUndoHistory();
                InvalidateVisual();
            }
        }

        public MarkdownEditorControl()
        {
            Focusable = true;
        }

        // Fonts / sizes
        private const double BaseFontSize = 14.0;
        private const double LineSpacing = 1.4;
        private const double LeftPadding = 6.0;

        // Tab configuration: 1 tab = TabSize columns
        private const int TabSize = 4;

        private double LineHeight => BaseFontSize * LineSpacing;

        private readonly Typeface _baseTypeface =
            new(new FontFamily("Consolas"), FontStyle.Normal, FontWeight.Normal);

        // Brushes
        private readonly IBrush _backgroundBrush =
            new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));

        private readonly IBrush _foregroundBrush = Brushes.White;

        private readonly IBrush _selectionBrush =
            new SolidColorBrush(Color.FromArgb(0x80, 0x33, 0x99, 0xFF)); // semi-transparent

        // Markdown style brushes (block-level only)
        private readonly IBrush _headingBrush =
            new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)); // gold

        private readonly IBrush _listBrush =
            new SolidColorBrush(Color.FromRgb(0xA0, 0xC8, 0xFF)); // light blue

        private readonly IBrush _quoteBrush =
            new SolidColorBrush(Color.FromRgb(0x80, 0xFF, 0x80)); // light green

        private readonly IBrush _codeBrush =
            new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x99)); // peach

        private readonly IBrush _hrBrush =
            new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)); // gray

        // Monospaced character width cache (for one column)
        private double _charWidth = -1;

        private double GetCharWidth()
        {
            if (_charWidth > 0)
                return _charWidth;

            var ft = new FormattedText(
                "M", // any visible character
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                GetTypeface(),
                BaseFontSize,
                _foregroundBrush);

            _charWidth = ft.Width;
            return _charWidth;
        }

        /// <summary>
        /// Compute the number of visual columns contributed by a substring
        /// of a line, treating '\t' as jumps to tab stops.
        /// </summary>
        private int ComputeColumns(string line, int startColumn, int length)
        {
            if (line == null)
                return 0;

            int lineLen = line.Length;
            int endIndex = Math.Min(startColumn + length, lineLen);
            if (startColumn < 0 || startColumn >= endIndex)
                return 0;

            int col = 0;

            for (int i = startColumn; i < endIndex; i++)
            {
                char ch = line[i];
                if (ch == '\t')
                {
                    col = ((col / TabSize) + 1) * TabSize;
                }
                else
                {
                    col++;
                }
            }

            return col;
        }

        // Visual lines produced by layout
        private sealed class VisualLine
        {
            public int DocLineIndex;
            public int StartColumn;  // character index in the line
            public int Length;       // number of characters in this segment
            public bool IsFirstSegmentOfLogicalLine;
        }

        private readonly List<VisualLine> _visualLines = new();

        private Typeface GetTypeface()
        {
            // Base typeface for layout calculations
            return _baseTypeface;
        }

        // Width in pixels, based on tab-aware column count
        private double MeasureTextWidth(string text, double fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            double cw = GetCharWidth();
            int cols = ComputeColumns(text, 0, text.Length);
            return cw * cols;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // We only care about height; ScrollViewer wraps us
            double height;

            if (_visualLines.Count > 0)
                height = _visualLines.Count * LineHeight;
            else
                height = Document.Lines.Count * LineHeight;

            // Width 0 tells ScrollViewer "I'll stretch"; it will size us to viewport width
            return new Size(0, height);
        }

        // ----------------------------
        // Markdown block-level styling
        // ----------------------------

        private enum MarkdownLineKind
        {
            Normal,
            Heading,
            ListItem,
            BlockQuote,
            CodeFence,
            CodeBlock,
            HorizontalRule
        }

        private sealed class MarkdownLineInfo
        {
            public MarkdownLineKind Kind;
        }

        private readonly List<MarkdownLineInfo> _markdownLines = new();

        private void EnsureMarkdownStyles()
        {
            _markdownLines.Clear();

            bool inCodeBlock = false;
            char codeFenceChar = '`'; // or '~'
            int codeFenceCount = 0;

            for (int i = 0; i < Document.Lines.Count; i++)
            {
                string raw = Document.Lines[i] ?? string.Empty;
                string trimmed = raw.TrimStart();

                var info = new MarkdownLineInfo { Kind = MarkdownLineKind.Normal };

                if (inCodeBlock)
                {
                    // Inside fenced code block
                    info.Kind = MarkdownLineKind.CodeBlock;

                    // Detect closing fence
                    if (IsCodeFence(trimmed, out char fenceChar2, out int count2) &&
                        fenceChar2 == codeFenceChar &&
                        count2 >= codeFenceCount)
                    {
                        info.Kind = MarkdownLineKind.CodeFence;
                        inCodeBlock = false;
                    }

                    _markdownLines.Add(info);
                    continue;
                }

                // Outside code block: detect fence first
                if (IsCodeFence(trimmed, out char fenceChar, out int count))
                {
                    info.Kind = MarkdownLineKind.CodeFence;
                    inCodeBlock = true;
                    codeFenceChar = fenceChar;
                    codeFenceCount = count;
                    _markdownLines.Add(info);
                    continue;
                }

                // Horizontal rule
                if (IsHorizontalRule(trimmed))
                {
                    info.Kind = MarkdownLineKind.HorizontalRule;
                    _markdownLines.Add(info);
                    continue;
                }

                // Heading: '#', '##', ...
                if (IsHeading(trimmed))
                {
                    info.Kind = MarkdownLineKind.Heading;
                    _markdownLines.Add(info);
                    continue;
                }

                // List item
                if (IsListItem(trimmed))
                {
                    info.Kind = MarkdownLineKind.ListItem;
                    _markdownLines.Add(info);
                    continue;
                }

                // Block quote
                if (trimmed.StartsWith(">"))
                {
                    info.Kind = MarkdownLineKind.BlockQuote;
                    _markdownLines.Add(info);
                    continue;
                }

                // Default: normal
                info.Kind = MarkdownLineKind.Normal;
                _markdownLines.Add(info);
            }
        }

        private static bool IsCodeFence(string trimmed, out char fenceChar, out int count)
        {
            fenceChar = '\0';
            count = 0;

            if (string.IsNullOrEmpty(trimmed))
                return false;

            char c = trimmed[0];
            if (c != '`' && c != '~')
                return false;

            int i = 0;
            while (i < trimmed.Length && trimmed[i] == c)
            {
                i++;
            }

            if (i < 3)
                return false;

            fenceChar = c;
            count = i;
            return true;
        }

        private static bool IsHorizontalRule(string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed))
                return false;

            // Strip spaces
            string s = trimmed.Replace(" ", "");

            if (s.Length < 3)
                return false;

            char c = s[0];
            if (c != '-' && c != '*' && c != '_')
                return false;

            foreach (char ch in s)
            {
                if (ch != c)
                    return false;
            }

            return true;
        }

        private static bool IsHeading(string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed))
                return false;

            int i = 0;
            int count = 0;
            while (i < trimmed.Length && trimmed[i] == '#')
            {
                count++;
                i++;
            }

            if (count == 0 || count > 6)
                return false;

            if (i >= trimmed.Length || trimmed[i] != ' ')
                return false;

            return true;
        }

        private static bool IsListItem(string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed))
                return false;

            // Bullet: -, *, + followed by space
            if ((trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') &&
                trimmed.Length > 1 && trimmed[1] == ' ')
                return true;

            // Numbered: "1. ", "23. "
            int i = 0;
            while (i < trimmed.Length && char.IsDigit(trimmed[i]))
                i++;

            if (i == 0 || i + 1 >= trimmed.Length)
                return false;

            if (trimmed[i] == '.' && trimmed[i + 1] == ' ')
                return true;

            return false;
        }

        private MarkdownLineKind GetLineKind(int docLineIndex)
        {
            if (docLineIndex < 0 || docLineIndex >= _markdownLines.Count)
                return MarkdownLineKind.Normal;

            // Current caret line is always treated as Normal (raw)
            if (docLineIndex == Document.CaretLine)
                return MarkdownLineKind.Normal;

            return _markdownLines[docLineIndex].Kind;
        }

        private Typeface GetTypefaceForLineKind(MarkdownLineKind kind)
        {
            // Keep monospaced metrics; only vary weight where it doesn't break layout badly
            return kind switch
            {
                MarkdownLineKind.Heading => new Typeface(_baseTypeface.FontFamily, FontStyle.Normal, FontWeight.Bold),
                MarkdownLineKind.ListItem => new Typeface(_baseTypeface.FontFamily, FontStyle.Normal, FontWeight.Normal),
                MarkdownLineKind.BlockQuote => new Typeface(_baseTypeface.FontFamily, FontStyle.Italic, FontWeight.Normal),
                MarkdownLineKind.CodeFence => new Typeface(_baseTypeface.FontFamily, FontStyle.Normal, FontWeight.Bold),
                MarkdownLineKind.CodeBlock => new Typeface(_baseTypeface.FontFamily, FontStyle.Normal, FontWeight.Normal),
                MarkdownLineKind.HorizontalRule => new Typeface(_baseTypeface.FontFamily, FontStyle.Normal, FontWeight.Normal),
                _ => _baseTypeface
            };
        }

        private IBrush GetBrushForLineKind(MarkdownLineKind kind)
        {
            return kind switch
            {
                MarkdownLineKind.Heading => _headingBrush,
                MarkdownLineKind.ListItem => _listBrush,
                MarkdownLineKind.BlockQuote => _quoteBrush,
                MarkdownLineKind.CodeFence => _codeBrush,
                MarkdownLineKind.CodeBlock => _codeBrush,
                MarkdownLineKind.HorizontalRule => _hrBrush,
                _ => _foregroundBrush
            };
        }

        // High-level parsed model (blocks, including tables)
        private MarkdownDocumentModel? _model;

        private void EnsureModel()
        {
            // For now we just reparse; later you can add dirty flags if needed
            _model = MarkdownBlockParser.Parse(Document);
        }        
        private TableBlock? FindTableBlockAtOffset(int offset)
        {
            if (_model == null)
                return null;

            foreach (var block in _model.Blocks)
            {
                if (block.Kind != MarkdownBlockKind.Table)
                    continue;

                if (block.Span.Contains(offset))
                    return (TableBlock)block;
            }

            return null;
        }

    }
}
