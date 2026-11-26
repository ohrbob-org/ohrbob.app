using Avalonia.Controls;

namespace Ohrbob.App.Views;

public partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        Loaded += (_, _) => ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (Bounds.Width <= 1) return;

        var narrow = Bounds.Width < 880;
        OnboardingHero.IsVisible = !narrow;
        OnboardingBody.ColumnDefinitions = narrow
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,470");

        Grid.SetColumn(ConnectPanel, narrow ? 0 : 1);
    }
}
