using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ShadowLink.Services;

internal sealed class SimpleChoiceDialogWindow : Window
{
    public SimpleChoiceDialogWindow(String title, String detail, String primaryLabel, String secondaryLabel)
    {
        Title = title;
        Width = 520;
        Height = 280;
        MinWidth = 520;
        MinHeight = 280;
        CanResize = false;
        CanMaximize = false;
        CanMinimize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#0F1720"));
        Icon = AppWindowIconLoader.Load();
        AutomationProperties.SetName(this, title);

        Button primaryButton = new Button
        {
            Content = primaryLabel,
            HorizontalAlignment = HorizontalAlignment.Left,
            TabIndex = 10
        };
        primaryButton.Classes.Add("primary-action");
        AutomationProperties.SetName(primaryButton, primaryLabel);
        primaryButton.Click += (_, _) => Close(true);

        Button secondaryButton = new Button
        {
            Content = secondaryLabel,
            HorizontalAlignment = HorizontalAlignment.Left,
            TabIndex = 20
        };
        secondaryButton.Classes.Add("secondary-action");
        AutomationProperties.SetName(secondaryButton, secondaryLabel);
        secondaryButton.Click += (_, _) => Close(false);

        Content = new Border
        {
            Padding = new Thickness(28),
            Child = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 30,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = detail,
                        Foreground = new SolidColorBrush(Color.Parse("#B4C2D3")),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            primaryButton,
                            secondaryButton
                        }
                    }
                }
            }
        };
    }
}
