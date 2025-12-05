using System;
using System.Collections.Generic;
using System.Net.Security;

namespace CanvasBoard.App.Markdown.Model
{
    public sealed class MarkdownDocumentModel
    {
        public IReadOnlyList<MarkdownBlock> Blocks { get; }

        public MarkdownDocumentModel(IReadOnlyList<MarkdownBlock> blocks)
        {
            Blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        }
    }
}
