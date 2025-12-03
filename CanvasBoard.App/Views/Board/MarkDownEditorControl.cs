using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CanvasBoard.App.Views.Board
{
    public class MarkdownEditorControl : Control
    {
        public MarkdownDocument Document { get; } = new();

        private readonly Typeface _typeface =
            new(new FontFamily("Consolas"), FontStyle.Normal, FontWeight.Normal);

        private const double FontSize = 14.0;
        private const double LineSpacing = 1.4; // multiplier

        public static readonly DirectProperty<MarkdownEditorControl, string> TextProperty =
            AvaloniaProperty.RegisterDirect<MarkdownEditorControl, string>(
                nameof(Text),
                o => o.Text,
                (o, v) => o.Text = v,
                string.Empty);

        private string _text = string.Empty;
        public string Text
        {
            get => _text;
            set
            {
                var val = value ?? string.Empty;
                if (_text == val)
                    return;

                _text = val;
                Document.SetText(_text);
                InvalidateVisual();
            }
        }

        public MarkdownEditorControl()
        {
            Focusable = true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return availableSize;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var lineHeight = FontSize * LineSpacing;

            // Background
            var bg = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
            context.FillRectangle(bg, new Rect(Bounds.Size));

            var brush = Brushes.White;

            for (int i = 0; i < Document.Lines.Count; i++)
            {
                var line = Document.Lines[i] ?? string.Empty;

                var ft = new FormattedText(
                    line,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    FontSize,
                    brush);

                var y = i * lineHeight;
                context.DrawText(ft, new Point(4, y)); // small left padding
            }

            // Caret
            DrawCaret(context, lineHeight);
        }

        private void DrawCaret(DrawingContext context, double lineHeight)
        {
            int line = Document.CaretLine;
            int col = Document.CaretColumn;

            if (line < 0 || line >= Document.Lines.Count)
                return;

            var lineText = Document.Lines[line] ?? string.Empty;
            col = Math.Clamp(col, 0, lineText.Length);

            double x = 4; // left padding
            if (col > 0)
            {
                var prefix = lineText.Substring(0, col);

                var ft = new FormattedText(
                    prefix,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    FontSize,
                    Brushes.White);

                x += ft.Width;
            }

            double yTop = line * lineHeight;
            double yBottom = yTop + lineHeight;

            var caretPen = new Pen(Brushes.White, 1);
            context.DrawLine(caretPen, new Point(x, yTop), new Point(x, yBottom));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            Focus();

            var p = e.GetPosition(this);
            var lineHeight = FontSize * LineSpacing;
            int line = (int)(p.Y / lineHeight);
            line = Math.Clamp(line, 0, Document.Lines.Count - 1);

            var lineText = Document.Lines[line] ?? string.Empty;
            int column = GetColumnFromX(lineText, p.X - 4); // minus padding

            Document.SetCaret(line, column);
            InvalidateVisual();

            e.Handled = true;
        }

        private int GetColumnFromX(string lineText, double x)
        {
            if (x <= 0)
                return 0;

            int bestCol = 0;
            double bestDiff = double.MaxValue;

            for (int col = 0; col <= lineText.Length; col++)
            {
                var prefix = lineText.Substring(0, col);

                var ft = new FormattedText(
                    prefix,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    FontSize,
                    Brushes.White);

                double width = ft.Width;
                double diff = Math.Abs(width - x);

                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestCol = col;
                }
            }

            return bestCol;
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            if (string.IsNullOrEmpty(e.Text))
                return;

            Document.InsertText(e.Text);
            _text = Document.GetText();
            InvalidateVisual();

            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.Key)
            {
                case Key.Left:
                    Document.MoveCaretLeft();
                    break;
                case Key.Right:
                    Document.MoveCaretRight();
                    break;
                case Key.Up:
                    Document.MoveCaretUp();
                    break;
                case Key.Down:
                    Document.MoveCaretDown();
                    break;
                case Key.Home:
                    Document.MoveCaretToLineStart();
                    break;
                case Key.End:
                    Document.MoveCaretToLineEnd();
                    break;
                case Key.Enter:
                    Document.InsertNewLine();
                    break;
                case Key.Back:
                    Document.Backspace();
                    break;
                case Key.Delete:
                    Document.Delete();
                    break;
                default:
                    return;
            }

            _text = Document.GetText();
            InvalidateVisual();
            e.Handled = true;
        }
    }
}
