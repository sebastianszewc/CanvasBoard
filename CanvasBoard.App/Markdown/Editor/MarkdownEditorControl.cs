using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CanvasBoard.App.Markdown.Document;

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
            return _baseTypeface;
        }

        // Width in pixels, based on tab-aware column count
        private double MeasureTextWidth(string text, double fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            double cw = GetCharWidth();
            // Here "length" is in characters; ComputeColumns will expand tabs.
            int cols = ComputeColumns(text, 0, text.Length);
            return cw * cols;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // Let the parent decide; we just fill available
            return availableSize;
        }
    }
}
