using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lockerit.Core.Security;

namespace Lockerit.App.Security;

internal sealed class WindowsPasswordDialog : Window
{
    private readonly PasswordBox _passwordBox = new();

    public WindowsPasswordDialog(WindowsAccountInfo account)
    {
        Title = "Unlock Lockerit";
        Width = 420;
        Height = 286;
        MinWidth = 420;
        MinHeight = 286;
        MaxWidth = 420;
        MaxHeight = 286;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushFrom("#0E0F0D");
        Foreground = BrushFrom("#F2EFE7");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        var root = new Border
        {
            Background = BrushFrom("#171816"),
            BorderBrush = BrushFrom("#34352F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22)
        };

        var panel = new DockPanel
        {
            LastChildFill = true
        };

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        DockPanel.SetDock(actions, Dock.Bottom);

        var cancelButton = CreateButton("Cancel", isPrimary: false);
        cancelButton.Margin = new Thickness(0, 0, 8, 0);
        cancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var unlockButton = CreateButton("Unlock", isPrimary: true);
        unlockButton.Click += (_, _) => Submit();

        actions.Children.Add(cancelButton);
        actions.Children.Add(unlockButton);

        var content = new StackPanel();

        content.Children.Add(new TextBlock
        {
            Text = "Windows authorization",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFrom("#F2EFE7")
        });

        content.Children.Add(new TextBlock
        {
            Text = $"Confirm the password for {account.DisplayName}.",
            Foreground = BrushFrom("#A9A39A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18)
        });

        content.Children.Add(new TextBlock
        {
            Text = "Password",
            Foreground = BrushFrom("#A9A39A"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        _passwordBox.MinHeight = 40;
        _passwordBox.Padding = new Thickness(12, 8, 12, 8);
        _passwordBox.Background = BrushFrom("#121311");
        _passwordBox.Foreground = BrushFrom("#F2EFE7");
        _passwordBox.BorderBrush = BrushFrom("#34352F");
        _passwordBox.BorderThickness = new Thickness(1);
        _passwordBox.CaretBrush = BrushFrom("#D97757");
        _passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Submit();
            }
        };
        content.Children.Add(_passwordBox);

        panel.Children.Add(actions);
        panel.Children.Add(content);
        root.Child = panel;
        Content = root;

        Loaded += (_, _) => _passwordBox.Focus();
    }

    public string Password => _passwordBox.Password;

    public void ClearPassword()
    {
        _passwordBox.Clear();
    }

    private void Submit()
    {
        if (string.IsNullOrEmpty(_passwordBox.Password))
        {
            _passwordBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private static System.Windows.Controls.Button CreateButton(string text, bool isPrimary)
    {
        return new System.Windows.Controls.Button
        {
            Content = text,
            MinWidth = 92,
            MinHeight = 38,
            Padding = new Thickness(14, 8, 14, 8),
            Background = isPrimary ? BrushFrom("#D97757") : BrushFrom("#20211E"),
            BorderBrush = isPrimary ? BrushFrom("#D97757") : BrushFrom("#34352F"),
            Foreground = isPrimary ? BrushFrom("#0E0F0D") : BrushFrom("#F2EFE7"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal
        };
    }

    private static System.Windows.Media.Brush BrushFrom(string color)
    {
        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }
}
