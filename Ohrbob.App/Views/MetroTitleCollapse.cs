using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ohrbob.App.Views;

/// <summary>
/// Shrinks a page title as its content scrolls. The title and the host that
/// reserves its height both shrink, so the header releases space to the
/// scrolling content instead of keeping a large empty band.
/// </summary>
public sealed class MetroTitleCollapse
{
    private readonly TextBlock _title;
    private readonly ScrollViewer _scroll;
    private readonly MetroTitleHost? _heightHost;
    private readonly ScaleTransform _transform = new(1, 1);
    private readonly List<Control> _fading = new();
    private readonly double _collapsedScale;
    private readonly double _distance;

    public MetroTitleCollapse(
        TextBlock title,
        ScrollViewer scroll,
        MetroTitleHost? heightHost = null,
        double collapsedScale = 0.45,
        double distance = 96)
    {
        _title = title;
        _scroll = scroll;
        _heightHost = heightHost;
        _collapsedScale = Math.Clamp(collapsedScale, 0.1, 1);
        _distance = Math.Max(1, distance);

        // Shrink from the top-left so the scaled text fits the shrunken host.
        _title.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        _title.RenderTransform = _transform;
        _title.VerticalAlignment = VerticalAlignment.Top;

        _scroll.ScrollChanged += OnScrollChanged;
        _title.PropertyChanged += OnTitlePropertyChanged;
        _title.Loaded += (_, _) => Apply();
        Apply();
    }

    /// <summary>Fades a supporting line (hint, subtitle) out as the title shrinks.</summary>
    public MetroTitleCollapse FadeWithCollapse(Control control)
    {
        control.Opacity = 1;
        _fading.Add(control);
        Apply();
        return this;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => Apply();

    private void OnTitlePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
            Apply();
    }

    private void Apply()
    {
        var progress = Math.Clamp(_scroll.Offset.Y / _distance, 0, 1);
        var scale = 1 + (_collapsedScale - 1) * progress;
        _transform.ScaleX = scale;
        _transform.ScaleY = scale;

        // The host measures the title at its natural height; only the host's
        // visible height shrinks, so the glyphs are never clamped or clipped.
        var naturalHeight = _heightHost?.NaturalHeight ?? 0;
        if (naturalHeight <= 0)
            naturalHeight = _title.LineHeight;
        if (double.IsNaN(naturalHeight) || naturalHeight <= 0)
            naturalHeight = _title.Bounds.Height;
        if (_heightHost is not null && naturalHeight > 0)
        {
            var hostHeight = naturalHeight * scale;
            if (double.IsNaN(_heightHost.VisibleHeight) ||
                Math.Abs(_heightHost.VisibleHeight - hostHeight) > 0.5)
                _heightHost.VisibleHeight = hostHeight;
        }

        var opacity = Math.Clamp(1 - progress * 1.4, 0, 1);
        foreach (var control in _fading)
            control.Opacity = opacity;
    }
}
