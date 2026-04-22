using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ShadowLink.Localization;

namespace ShadowLink.Services;

internal sealed class PassphraseDialogWindow : Window
{
    private readonly TextBox _passphraseTextBox;

    public PassphraseDialogWindow(String title, String detail)
    {
        Title = title;
        Width = 560;
        Height = 320;
        MinWidth = 560;
        MinHeight = 320;
        CanResize = false;
        CanMaximize = false;
        CanMinimize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#0F1720"));
        Icon = AppWindowIconLoader.Load();
        AutomationProperties.SetName(this, ShadowLinkText.Translate("dialog.passphrase.window"));

        _passphraseTextBox = new TextBox
        {
            PlaceholderText = ShadowLinkText.Translate("dialog.passphrase.placeholder"),
            PasswordChar = '*',
            TabIndex = 10
        };
        AutomationProperties.SetHelpText(_passphraseTextBox, ShadowLinkText.Translate("common.masked_input"));
        AutomationProperties.SetName(_passphraseTextBox, ShadowLinkText.Translate("common.passphrase"));

        Button connectButton = new Button
        {
            Content = ShadowLinkText.Translate("common.connect"),
            HorizontalAlignment = HorizontalAlignment.Left,
            TabIndex = 20
        };
        connectButton.Classes.Add("primary-action");
        AutomationProperties.SetName(connectButton, ShadowLinkText.Translate("dialog.passphrase.connect"));
        connectButton.Click += (_, _) => Close(String.IsNullOrWhiteSpace(_passphraseTextBox.Text) ? null : _passphraseTextBox.Text.Trim());

        Button cancelButton = new Button
        {
            Content = ShadowLinkText.Translate("common.cancel"),
            HorizontalAlignment = HorizontalAlignment.Left,
            TabIndex = 30
        };
        cancelButton.Classes.Add("secondary-action");
        AutomationProperties.SetName(cancelButton, ShadowLinkText.Translate("dialog.passphrase.cancel"));
        cancelButton.Click += (_, _) => Close(null);

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
                        Text = ShadowLinkText.Translate("dialog.passphrase.title"),
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
                    _passphraseTextBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            connectButton,
                            cancelButton
                        }
                    }
                }
            }
        };

        Opened += (_, _) => _passphraseTextBox.Focus();
    }
}
