using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using NUnit.Framework;

namespace Ohrbob.App.Tests;

/// <summary>
/// Dark mode support (issue #1): the app should follow the OS light/dark setting,
/// which means the custom brushes must resolve to different values per theme variant.
/// </summary>
[TestFixture]
public class ThemeModeTests
{
    private static Color Resolve(ThemeVariant variant, string key)
    {
        var app = Application.Current!;
        Assert.That(app.TryFindResource(key, variant, out var value), Is.True,
            $"resource '{key}' should exist for the {variant} theme variant");
        Assert.That(value, Is.InstanceOf<ISolidColorBrush>(),
            $"resource '{key}' should be a SolidColorBrush");
        return ((ISolidColorBrush)value!).Color;
    }

    [AvaloniaTest]
    public void AppBackgroundBrush_SwitchesWithTheThemeVariant()
    {
        var light = Resolve(ThemeVariant.Light, "AppBackgroundBrush");
        var dark = Resolve(ThemeVariant.Dark, "AppBackgroundBrush");

        Assert.That(dark, Is.Not.EqualTo(light),
            "AppBackgroundBrush must differ between light and dark themes");
    }

    [AvaloniaTest]
    public void DarkTheme_HasDarkBackgroundAndLightForeground()
    {
        var background = Resolve(ThemeVariant.Dark, "AppBackgroundBrush");
        var foreground = Resolve(ThemeVariant.Dark, "AppForegroundBrush");

        double Luminance(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        Assert.That(Luminance(background), Is.LessThan(80),
            "dark theme background should be dark");
        Assert.That(Luminance(foreground), Is.GreaterThan(180),
            "dark theme foreground should be light enough to read on a dark background");
    }

    private static double Chroma(Color c) =>
        Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));

    [AvaloniaTest]
    public void DarkTheme_BackgroundIsNeutralGrey()
    {
        var background = Resolve(ThemeVariant.Dark, "AppBackgroundBrush");

        Assert.That(Chroma(background), Is.LessThanOrEqualTo(12),
            "dark theme background should be a neutral grey, not a blue-tinted navy");
    }

    [AvaloniaTest]
    public void DarkTheme_SurfacesAreNeutralGrey()
    {
        var surface = Resolve(ThemeVariant.Dark, "AppSurfaceBrush");

        Assert.That(Chroma(surface), Is.LessThanOrEqualTo(12),
            "dark theme surfaces (cards, sidebar) should be neutral grey, not blue-tinted");
    }

    [AvaloniaTest]
    public void DarkTheme_CustomSurfaces_BecomeDark()
    {
        var surface = Resolve(ThemeVariant.Dark, "AppSurfaceBrush");
        var lightSurface = Resolve(ThemeVariant.Light, "AppSurfaceBrush");

        double Luminance(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        Assert.That(lightSurface, Is.Not.EqualTo(surface),
            "AppSurfaceBrush must switch with the theme variant");
        Assert.That(Luminance(surface), Is.LessThan(80),
            "dark theme surfaces (cards, sidebar) should be dark");
    }
}
