using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Lockerit.App;

internal sealed class TotpUnlockDialog : Window
{
    private readonly WpfTextBox _codeInput = new();
    private readonly TextBlock _validationText = new();

    private TotpUnlockDialog(int activeRecoveryCodeCount)
    {
        Title = "Two-Factor Authentication";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushFrom("#0E0F0D");
        Foreground = BrushFrom("#F2EFE7");

        var root = new Border
        {
            Background = BrushFrom("#171816"),
            BorderBrush = BrushFrom("#34352F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24)
        };

        var stack = new StackPanel();
        root.Child = stack;

        stack.Children.Add(new TextBlock
        {
            Text = "Two-factor code required",
            Foreground = BrushFrom("#F2EFE7"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });

        stack.Children.Add(new TextBlock
        {
            Text = activeRecoveryCodeCount > 0
                ? $"Enter the six-digit authenticator code, or one of your {activeRecoveryCodeCount} remaining recovery codes. If a fresh code fails, check automatic time on this PC and phone."
                : "Enter the six-digit authenticator code. If a fresh code fails, check automatic time on this PC and phone. No active recovery codes remain.",
            Foreground = BrushFrom("#A9A39A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Code",
            Foreground = BrushFrom("#A9A39A"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 6)
        });

        ConfigureTextBox(_codeInput);
        stack.Children.Add(_codeInput);

        _validationText.Foreground = BrushFrom("#E06A5F");
        _validationText.FontSize = 12;
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_validationText);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        var cancelButton = CreateButton("Cancel", BrushFrom("#20211E"), BrushFrom("#34352F"), BrushFrom("#F2EFE7"));
        cancelButton.Margin = new Thickness(0, 0, 8, 0);
        cancelButton.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancelButton);

        var verifyButton = CreateButton("Verify", BrushFrom("#D97757"), BrushFrom("#D97757"), BrushFrom("#0E0F0D"));
        verifyButton.FontWeight = FontWeights.SemiBold;
        verifyButton.Click += ConfirmButton_Click;
        actions.Children.Add(verifyButton);

        stack.Children.Add(actions);
        Content = root;

        Loaded += (_, _) => _codeInput.Focus();
    }

    public string Code { get; private set; } = string.Empty;

    public static string? ShowForUnlock(Window owner, int activeRecoveryCodeCount)
    {
        var dialog = new TotpUnlockDialog(activeRecoveryCodeCount)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.Code : null;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var code = _codeInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            _validationText.Text = "Enter an authenticator code or recovery code.";
            _codeInput.Focus();
            return;
        }

        Code = code;
        DialogResult = true;
    }

    private static void ConfigureTextBox(WpfTextBox textBox)
    {
        textBox.MinHeight = 38;
        textBox.Padding = new Thickness(12, 8, 12, 8);
        textBox.Background = BrushFrom("#121311");
        textBox.Foreground = BrushFrom("#F2EFE7");
        textBox.CaretBrush = BrushFrom("#D97757");
        textBox.BorderBrush = BrushFrom("#34352F");
        textBox.BorderThickness = new Thickness(1);
        textBox.FontFamily = new WpfFontFamily("Segoe UI");
    }

    private static WpfButton CreateButton(string text, WpfBrush background, WpfBrush border, WpfBrush foreground)
    {
        return new WpfButton
        {
            Content = text,
            MinHeight = 38,
            MinWidth = 92,
            Padding = new Thickness(14, 8, 14, 8),
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Foreground = foreground,
            Cursor = System.Windows.Input.Cursors.Hand,
            FontFamily = new WpfFontFamily("Segoe UI")
        };
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        return new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color));
    }
}
