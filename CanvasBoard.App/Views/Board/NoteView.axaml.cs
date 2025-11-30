using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace CanvasBoard.App.Views.Board;

public partial class NoteView : UserControl
{
    private TextBox _editor = null!;
    private Control _viewer = null!;
    private bool _isEditing;

    public NoteView()
    {
        InitializeComponent();

        _editor = this.FindControl<TextBox>("MarkdownEditor")
                  ?? throw new InvalidOperationException("MarkdownEditor not found.");
        _viewer = this.FindControl<Control>("MarkdownView")
                  ?? throw new InvalidOperationException("MarkdownView not found.");

        // Default: view mode (rendered markdown)
        _isEditing = false;
        UpdateMode();
        UpdateMarkdown();

        // Whenever user types, update the rendered markdown
        _editor.TextChanged += (_, _) => UpdateMarkdown();

        // Leave edit mode on focus loss
        _editor.LostFocus += (_, _) =>
        {
            if (IsEditing)
                IsEditing = false;
        };

        // Keyboard shortcuts: Esc or Ctrl+Enter to exit edit mode
        _editor.KeyDown += EditorOnKeyDown;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Public text API used by BoardView
    public string Text
    {
        get => _editor.Text ?? string.Empty;
        set
        {
            _editor.Text = value;
            UpdateMarkdown();
        }
    }

    // Public read-only flag BoardView can inspect
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value)
                return;

            _isEditing = value;
            UpdateMode();
        }
    }

    private void UpdateMode()
    {
        if (_editor == null || _viewer == null)
            return;

        if (_isEditing)
        {
            _editor.IsVisible = true;
            _viewer.IsVisible = false;

            _editor.Focus();
            if (!string.IsNullOrEmpty(_editor.Text))
                _editor.CaretIndex = _editor.Text.Length;
        }
        else
        {
            _editor.IsVisible = false;
            _viewer.IsVisible = true;

            UpdateMarkdown();
        }
    }

    private void UpdateMarkdown()
    {
        if (_viewer is { } v)
        {
            // MarkdownScrollViewer has .Markdown property
            // but it's not on a common interface, so use dynamic
            dynamic dyn = v;
            dyn.Markdown = _editor.Text ?? string.Empty;
        }
    }

    private void EditorOnKeyDown(object? sender, KeyEventArgs e)
    {
        // ESC = leave edit mode
        if (e.Key == Key.Escape)
        {
            IsEditing = false;
            e.Handled = true;
            return;
        }

        // Ctrl+Enter = leave edit mode
        if (e.Key == Key.Enter &&
            (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            IsEditing = false;
            e.Handled = true;
        }
    }

    // Double-click anywhere on the note border => enter edit mode
    private void OnBorderDoubleTapped(object? sender, RoutedEventArgs e)
    {
        IsEditing = true;
        e.Handled = true;
    }
}
