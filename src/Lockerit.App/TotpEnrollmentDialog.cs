using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lockerit.Core.Security;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Lockerit.App;

internal sealed class TotpEnrollmentDialog : Window
{
    private readonly TotpEnrollment _enrollment;
    private readonly WpfTextBox _codeInput = new();
    private readonly TextBlock _validationText = new();

    private TotpEnrollmentDialog(TotpEnrollment enrollment)
    {
        _enrollment = enrollment;

        Title = "Two-Factor Authentication";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushFrom("#0E0F0D");
        Foreground = BrushFrom("#F2EFE7");

        var root = CreateRoot();
        var stack = new StackPanel();
        root.Child = stack;

        stack.Children.Add(CreateHeading("Set up authenticator"));
        stack.Children.Add(CreateBody("Add this LockerIt vault to an authenticator app, then enter the six-digit code shown by the app. Keep the recovery codes after setup; they are shown once."));

        stack.Children.Add(CreateLabel("Manual setup key"));
        var secretRow = CreateCopyRow(enrollment.SecretBase32, "Copy key");
        stack.Children.Add(secretRow);

        stack.Children.Add(CreateLabel("Setup URI"));
        var uriRow = CreateCopyRow(enrollment.SetupUri, "Copy URI");
        stack.Children.Add(uriRow);

        stack.Children.Add(CreateLabel("Authenticator code"));
        ConfigureTextBox(_codeInput);
        _codeInput.MaxLength = 12;
        stack.Children.Add(_codeInput);

        _validationText.Foreground = BrushFrom("#E06A5F");
        _validationText.FontSize = 12;
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_validationText);

        stack.Children.Add(CreateActions("Enable 2FA", ConfirmButton_Click));
        Content = root;

        Loaded += (_, _) => _codeInput.Focus();
    }

    public string VerificationCode { get; private set; } = string.Empty;

    public static string? ShowForSetup(Window owner, TotpEnrollment enrollment)
    {
        var dialog = new TotpEnrollmentDialog(enrollment)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.VerificationCode : null;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var code = TotpAuthenticator.NormalizeCode(_codeInput.Text);
        if (code.Length != 6)
        {
            _validationText.Text = "Enter the six-digit code from your authenticator app.";
            _codeInput.Focus();
            return;
        }

        VerificationCode = code;
        DialogResult = true;
    }

    private Grid CreateCopyRow(string value, string actionText)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new WpfTextBox
        {
            Text = value,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 38,
            MaxHeight = 90
        };
        ConfigureTextBox(textBox);
        grid.Children.Add(textBox);

        var copyButton = CreateButton(actionText, BrushFrom("#20211E"), BrushFrom("#34352F"), BrushFrom("#F2EFE7"));
        copyButton.Click += (_, _) => System.Windows.Clipboard.SetText(value);
        Grid.SetColumn(copyButton, 2);
        grid.Children.Add(copyButton);

        return grid;
    }

    private static Border CreateRoot()
    {
        return new Border
        {
            Background = BrushFrom("#171816"),
            BorderBrush = BrushFrom("#34352F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24)
        };
    }

    private static TextBlock CreateHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = BrushFrom("#F2EFE7"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        };
    }

    private static TextBlock CreateBody(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = BrushFrom("#A9A39A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18)
        };
    }

    private static TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = BrushFrom("#A9A39A"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 6)
        };
    }

    private StackPanel CreateActions(string primaryAction, RoutedEventHandler primaryHandler)
    {
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

        var primaryButton = CreateButton(primaryAction, BrushFrom("#D97757"), BrushFrom("#D97757"), BrushFrom("#0E0F0D"));
        primaryButton.FontWeight = FontWeights.SemiBold;
        primaryButton.Click += primaryHandler;
        actions.Children.Add(primaryButton);

        return actions;
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
