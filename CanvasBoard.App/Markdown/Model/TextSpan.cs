using System;

namespace CanvasBoard.App.Markdown.Model
{
    /// <summary>
    /// Half-open text span [Start, End) in document offset space.
    /// </summary>
    public readonly struct TextSpan
    {
        public int Start { get; }
        public int End { get; }
        public int Length => End - Start;

        public TextSpan(int start, int end)
        {
            if (end < start)
                throw new ArgumentException("End must be >= Start.", nameof(end));

            Start = start;
            End = end;
        }

        public bool Contains(int offset) => offset >= Start && offset < End;

        public static TextSpan FromLength(int start, int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            return new TextSpan(start, start + length);
        }

        public override string ToString() => $"[{Start}, {End})";
    }
}
