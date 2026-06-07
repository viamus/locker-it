using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lockerit.Core.Security;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;
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
        Width = 720;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushFrom("#0E0F0D");
        Foreground = BrushFrom("#F2EFE7");

        var root = CreateRoot();
        var stack = new StackPanel();
        root.Child = stack;

        stack.Children.Add(CreateHeading("Set up authenticator"));
        stack.Children.Add(CreateBody("Scan the QR code with an authenticator app, then enter the six-digit code shown by the app. Keep the recovery codes after setup; they are shown once."));

        var setupGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2)
        };
        setupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        setupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        setupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        setupGrid.Children.Add(CreateQrCodeCard(enrollment.SetupUri));

        var manualStack = new StackPanel();
        Grid.SetColumn(manualStack, 2);

        manualStack.Children.Add(CreateLabel("Manual setup key", topMargin: 0));
        manualStack.Children.Add(CreateCopyRow(enrollment.SecretBase32, "Copy key"));
        manualStack.Children.Add(CreateLabel("Setup URI"));
        manualStack.Children.Add(CreateCopyRow(enrollment.SetupUri, "Copy URI"));
        setupGrid.Children.Add(manualStack);

        stack.Children.Add(setupGrid);

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

    private Border CreateQrCodeCard(string setupUri)
    {
        return new Border
        {
            Width = 236,
            Background = BrushFrom("#121311"),
            BorderBrush = BrushFrom("#34352F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Scan QR code",
                        Foreground = BrushFrom("#F2EFE7"),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 10)
                    },
                    CreateQrCodeImage(setupUri),
                    new TextBlock
                    {
                        Text = "Use the manual key if your authenticator cannot scan it.",
                        Foreground = BrushFrom("#A9A39A"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 10, 0, 0)
                    }
                }
            }
        };
    }

    private static WpfImage CreateQrCodeImage(string setupUri)
    {
        var matrix = TotpQrCodeGenerator.CreateMatrix(setupUri);
        const int quietZone = 4;
        const int moduleSize = 4;
        var pixelSize = (matrix.Size + quietZone * 2) * moduleSize;
        var drawing = new DrawingGroup();

        using (var context = drawing.Open())
        {
            context.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, pixelSize, pixelSize));

            for (var y = 0; y < matrix.Size; y++)
            {
                for (var x = 0; x < matrix.Size; x++)
                {
                    if (!matrix.IsDark(x, y))
                    {
                        continue;
                    }

                    context.DrawRectangle(
                        System.Windows.Media.Brushes.Black,
                        null,
                        new Rect(
                            (x + quietZone) * moduleSize,
                            (y + quietZone) * moduleSize,
                            moduleSize,
                            moduleSize));
                }
            }
        }

        drawing.Freeze();
        return new WpfImage
        {
            Source = new DrawingImage(drawing),
            Width = 204,
            Height = 204,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true
        };
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

    private static TextBlock CreateLabel(string text, double topMargin = 12)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = BrushFrom("#A9A39A"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, topMargin, 0, 6)
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
