using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CanvasBoard.App.Views.Board;

public partial class NoteView : UserControl
{
    private MarkdownEditorControl _editor = null!;

    private string _text = "## New note\nEdit me.";
    public string Text
    {
        get => _editor?.Text ?? _text;
        set
        {
            _text = value ?? string.Empty;
            if (_editor != null)
                _editor.Text = _text;
        }
    }

    public bool IsEditing { get; private set; }

    public NoteView()
    {
        InitializeComponent();

        _editor = this.FindControl<MarkdownEditorControl>("Editor")
                  ?? throw new InvalidOperationException("Editor not found.");

        _editor.Text = _text;

        _editor.GotFocus += (_, _) => IsEditing = true;
        _editor.LostFocus += (_, _) => IsEditing = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
