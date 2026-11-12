using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Ohrbob.App.Services;

namespace Ohrbob.App.Views;

/// <summary>A TextBlock that renders http(s)/mailto URIs as underlined, clickable links.</summary>
public class LinkTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> SourceTextProperty =
        AvaloniaProperty.Register<LinkTextBlock, string?>(nameof(SourceText));

    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<LinkTextBlock, ICommand?>(nameof(LinkCommand));

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.FromRgb(0x54, 0x4F, 0xF4));
    private static readonly TextDecorationCollection LinkDecorations = new()
    {
        new TextDecoration { Location = TextDecorationLocation.Underline }
    };
    private readonly List<(int Start, int Length, Uri Uri)> _links = new();

    public string? SourceText
    {
        get => GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceTextProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        Inlines = new InlineCollection();
        _links.Clear();

        var offset = 0;
        foreach (var segment in LinkParser.Parse(SourceText))
        {
            if (segment.Uri is null)
            {
                Inlines.Add(new Run(segment.Text));
            }
            else
            {
                Inlines.Add(new Run(segment.Text)
                {
                    Foreground = LinkBrush,
                    TextDecorations = LinkDecorations
                });
                _links.Add((offset, segment.Text.Length, segment.Uri));
            }

            offset += segment.Text.Length;
        }
    }

    /// <summary>Returns the link covering a character index, for tests and hit testing.</summary>
    public Uri? LinkAt(int textPosition)
    {
        foreach (var link in _links)
        {
            if (textPosition >= link.Start && textPosition < link.Start + link.Length)
                return link.Uri;
        }

        return null;
    }

    /// <summary>Executes the link command for a character index. Returns true when handled.</summary>
    public bool TryOpenLinkAt(int textPosition)
    {
        if (LinkAt(textPosition) is not { } uri || LinkCommand?.CanExecute(uri) != true)
            return false;

        LinkCommand.Execute(uri);
        return true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        if (TextLayout is { } layout &&
            layout.HitTestPoint(point) is { IsInside: true } hit &&
            TryOpenLinkAt(hit.TextPosition))
        {
            e.Handled = true;
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var overLink = TextLayout is { } layout &&
                       layout.HitTestPoint(point) is { IsInside: true } hit &&
                       LinkAt(hit.TextPosition) is not null;
        Cursor = overLink ? HandCursor : ArrowCursor;
        base.OnPointerMoved(e);
    }
}
