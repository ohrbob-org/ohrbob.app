# Avalonia Styleguide for Ohrbob App

This styleguide ensures your cross-platform Avalonia application exactly matches the Bootstrap-based theme defined in the `ohrbob-bootstrap` web repository.

## 1. Color Palette

Define the Ohrbob colors as `SolidColorBrush` resources in your `App.axaml` (or a dedicated `ResourceDictionary` like `Colors.axaml`). These perfectly match the SCSS `$variables`.

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Base Colors -->
    <SolidColorBrush x:Key="OhrbobDarkBlue">#001957</SolidColorBrush>
    <SolidColorBrush x:Key="OhrbobLightBlue">#544ff4</SolidColorBrush>
    <SolidColorBrush x:Key="OhrbobYellow">#fdb200</SolidColorBrush>
    <SolidColorBrush x:Key="OhrbobOrangeLight">#ff9a00</SolidColorBrush>
    <SolidColorBrush x:Key="OhrbobStrongOrange">#fc5d00</SolidColorBrush>
    <SolidColorBrush x:Key="OhrbobPaleYellow">#fff4d8</SolidColorBrush>
    <SolidColorBrush x:Key="OhrbobPaleViolet">#e9e8fd</SolidColorBrush>
    
    <!-- Bootstrap Theme Mapping -->
    <SolidColorBrush x:Key="ThemePrimaryBrush" Color="{StaticResource OhrbobStrongOrange}" />
    <SolidColorBrush x:Key="ThemeSecondaryBrush" Color="{StaticResource OhrbobLightBlue}" />
    <SolidColorBrush x:Key="ThemeInfoBrush" Color="{StaticResource OhrbobLightBlue}" />
    <SolidColorBrush x:Key="ThemeWarningBrush" Color="{StaticResource OhrbobYellow}" />
    <SolidColorBrush x:Key="ThemeDangerBrush">#dc3545</SolidColorBrush>
    <SolidColorBrush x:Key="ThemeLightBrush" Color="{StaticResource OhrbobPaleYellow}" />
    <SolidColorBrush x:Key="ThemeDarkBrush" Color="{StaticResource OhrbobDarkBlue}" />

    <!-- Application Background & Foreground -->
    <SolidColorBrush x:Key="AppBackgroundBrush">#ffffff</SolidColorBrush>
    <SolidColorBrush x:Key="AppForegroundBrush" Color="{StaticResource OhrbobDarkBlue}" />
</ResourceDictionary>
```

## 2. Typography

The web app uses the **Sulphur Point** font for headings, with a default fallback to sans-serif. In Avalonia, you bundle the TTF files as `AvaloniaResource` in your `.csproj`.

### Font Definition
Add the fonts to an `Assets/Fonts/` folder in your Avalonia project. In your `App.axaml`:

```xml
<Application.Resources>
    <FontFamily x:Key="SulphurPoint">avares://YourAssemblyName/Assets/Fonts#Sulphur Point</FontFamily>
</Application.Resources>
```

### Text Styles
Map Bootstrap heading levels (`h1`, `h2`, etc.) to Avalonia `TextBlock` style classes:

```xml
<Style Selector="TextBlock.h1">
    <Setter Property="FontFamily" Value="{StaticResource SulphurPoint}"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="FontSize" Value="32"/>
    <Setter Property="Foreground" Value="{StaticResource AppForegroundBrush}"/>
</Style>

<Style Selector="TextBlock.h2">
    <Setter Property="FontFamily" Value="{StaticResource SulphurPoint}"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="FontSize" Value="24"/>
    <Setter Property="Foreground" Value="{StaticResource AppForegroundBrush}"/>
</Style>
```
*Usage:* `<TextBlock Classes="h1" Text="Hello World"/>`

## 3. Component Styles (Bootstrap Classes)

In Avalonia, Bootstrap classes like `.btn-primary` are implemented using Style Selectors mapping to `Classes="btn-primary"`.

### Buttons
```xml
<Style Selector="Button">
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12,6"/>
</Style>

<!-- .btn-primary -->
<Style Selector="Button.btn-primary">
    <Setter Property="Background" Value="{StaticResource ThemePrimaryBrush}"/>
    <Setter Property="Foreground" Value="White"/>
</Style>
<Style Selector="Button.btn-primary:pointerover /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Background" Value="{StaticResource OhrbobOrangeLight}"/>
</Style>

<!-- .btn-secondary -->
<Style Selector="Button.btn-secondary">
    <Setter Property="Background" Value="{StaticResource ThemeSecondaryBrush}"/>
    <Setter Property="Foreground" Value="White"/>
</Style>
<Style Selector="Button.btn-secondary:pointerover /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Background" Value="{StaticResource OhrbobPaleViolet}"/>
    <Setter Property="Foreground" Value="{StaticResource ThemeDarkBrush}"/>
</Style>
```
*Usage:* `<Button Classes="btn-primary" Content="Click Me" />`

## 4. Window and Container Backgrounds

Set default window styles to match `$body-bg` and `$body-color`:

```xml
<Style Selector="Window">
    <Setter Property="Background" Value="{StaticResource AppBackgroundBrush}"/>
    <Setter Property="Foreground" Value="{StaticResource AppForegroundBrush}"/>
</Style>
```

## Summary
By injecting the specific hex codes from `$variables` into Avalonia `SolidColorBrush` resources, mapping standard Bootstrap modifier classes to Avalonia `Classes="btn-xxx"`, and embedding `Sulphur Point` as an `avares://` resource, the UI will retain total visual parity with the web interface.
