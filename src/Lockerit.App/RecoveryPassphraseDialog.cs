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

internal sealed class RecoveryPassphraseDialog : Window
{
    private readonly PasswordBox _passphraseInput = new();
    private readonly PasswordBox? _confirmInput;
    private readonly WpfTextBox? _hintInput;
    private readonly TextBlock _validationText = new();
    private readonly bool _requiresConfirmation;

    private RecoveryPassphraseDialog(
        string title,
        string heading,
        string description,
        string primaryAction,
        bool requiresConfirmation,
        bool includesHint,
        string? passphraseHint)
    {
        _requiresConfirmation = requiresConfirmation;

        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushFrom("#0E0F0D");
        Foreground = BrushFrom("#F2EFE7");

        _confirmInput = requiresConfirmation ? new PasswordBox() : null;
        _hintInput = includesHint ? new WpfTextBox() : null;

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
            Text = heading,
            Foreground = BrushFrom("#F2EFE7"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });

        stack.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = BrushFrom("#A9A39A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18)
        });

        stack.Children.Add(CreateLabel("Recovery passphrase"));
        ConfigurePasswordBox(_passphraseInput);
        stack.Children.Add(_passphraseInput);

        if (_confirmInput is not null)
        {
            stack.Children.Add(CreateLabel("Confirm passphrase"));
            ConfigurePasswordBox(_confirmInput);
            stack.Children.Add(_confirmInput);
        }

        if (_hintInput is not null)
        {
            stack.Children.Add(CreateLabel("Passphrase hint (optional, not secret)"));
            ConfigureTextBox(_hintInput);
            stack.Children.Add(_hintInput);
        }
        else if (!string.IsNullOrWhiteSpace(passphraseHint))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Hint: {passphraseHint}",
                Foreground = BrushFrom("#A9A39A"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0)
            });
        }

        _validationText.Foreground = BrushFrom("#E06A5F");
        _validationText.FontSize = 12;
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.Margin = new Thickness(0, 12, 0, 0);
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

        var primaryButton = CreateButton(primaryAction, BrushFrom("#D97757"), BrushFrom("#D97757"), BrushFrom("#0E0F0D"));
        primaryButton.FontWeight = FontWeights.SemiBold;
        primaryButton.Click += ConfirmButton_Click;
        actions.Children.Add(primaryButton);

        stack.Children.Add(actions);
        LockeritWindowChrome.Install(this, root, canResize: false);

        Loaded += (_, _) => _passphraseInput.Focus();
    }

    public string Passphrase { get; private set; } = string.Empty;
    public string PassphraseHint { get; private set; } = string.Empty;

    public static RecoveryPassphraseRequest? ShowForExport(Window owner)
    {
        var dialog = new RecoveryPassphraseDialog(
            "Export Recovery Kit",
            "Export Recovery Kit",
            "Create a recovery passphrase. You will need it with the vault database to unlock this vault on another Windows account.",
            "Export",
            requiresConfirmation: true,
            includesHint: true,
            passphraseHint: null)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true
            ? new RecoveryPassphraseRequest(dialog.Passphrase, dialog.PassphraseHint)
            : null;
    }

    public static string? ShowForImport(Window owner, string? passphraseHint)
    {
        var dialog = new RecoveryPassphraseDialog(
            "Import Recovery Kit",
            "Import Recovery Kit",
            "Enter the recovery passphrase for this kit. Lockerit will create a new Windows-protected keyring for the current account.",
            "Import",
            requiresConfirmation: false,
            includesHint: false,
            passphraseHint: passphraseHint)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _passphraseInput.Password = string.Empty;
        if (_confirmInput is not null)
        {
            _confirmInput.Password = string.Empty;
        }

        if (_hintInput is not null)
        {
            _hintInput.Text = string.Empty;
        }

        if (DialogResult != true)
        {
            Passphrase = string.Empty;
            PassphraseHint = string.Empty;
        }

        base.OnClosed(e);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var passphrase = _passphraseInput.Password;

        if (string.IsNullOrWhiteSpace(passphrase))
        {
            _validationText.Text = "Recovery passphrase is required.";
            _passphraseInput.Focus();
            return;
        }

        if (_requiresConfirmation && passphrase.Length < 12)
        {
            _validationText.Text = "Use at least 12 characters for the recovery passphrase.";
            _passphraseInput.Focus();
            return;
        }

        if (_confirmInput is not null && !string.Equals(passphrase, _confirmInput.Password, StringComparison.Ordinal))
        {
            _validationText.Text = "The passphrases do not match.";
            _confirmInput.Focus();
            return;
        }

        Passphrase = passphrase;
        PassphraseHint = _hintInput?.Text.Trim() ?? string.Empty;
        DialogResult = true;
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

    private static void ConfigurePasswordBox(PasswordBox passwordBox)
    {
        passwordBox.MinHeight = 38;
        passwordBox.Padding = new Thickness(12, 8, 12, 8);
        passwordBox.Background = BrushFrom("#121311");
        passwordBox.Foreground = BrushFrom("#F2EFE7");
        passwordBox.CaretBrush = BrushFrom("#D97757");
        passwordBox.BorderBrush = BrushFrom("#34352F");
        passwordBox.BorderThickness = new Thickness(1);
        passwordBox.FontFamily = new WpfFontFamily("Segoe UI");
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

internal sealed record RecoveryPassphraseRequest(string Passphrase, string PassphraseHint);
