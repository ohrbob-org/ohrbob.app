using System;
using Avalonia;
using Avalonia.Controls;

namespace Ohrbob.App.Views;

public partial class MainView : UserControl
{
    public static readonly StyledProperty<bool> IsPortraitProperty =
        AvaloniaProperty.Register<MainView, bool>(nameof(IsPortrait));

    public bool IsPortrait
    {
        get => GetValue(IsPortraitProperty);
        set => SetValue(IsPortraitProperty, value);
    }

    public MainView()
    {
        InitializeComponent();
        
        PropertyChanged += (s, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                var bounds = Bounds;
                bool newIsPortrait = bounds.Width < bounds.Height;
                if (IsPortrait != newIsPortrait)
                {
                    IsPortrait = newIsPortrait;
                    if (DataContext is ViewModels.MainViewModel vm)
                    {
                        vm.IsPortrait = newIsPortrait;
                        if (newIsPortrait) vm.IsSidebarOpen = false;
                    }
                }
            }
        };
    }
}