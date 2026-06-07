using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Lockerit.App;

internal sealed class MasterPasswordDialog : Window
{
    private readonly PasswordBox _passwordInput = new();
    private readonly PasswordBox? _confirmInput;
    private readonly TextBlock _validationText = new();
    private readonly bool _requiresConfirmation;

    private MasterPasswordDialog(
        string title,
        string heading,
        string description,
        string primaryAction,
        bool requiresConfirmation)
    {
        _requiresConfirmation = requiresConfirmation;
        _confirmInput = requiresConfirmation ? new PasswordBox() : null;

        Title = title;
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

        stack.Children.Add(CreateLabel("Master password"));
        ConfigurePasswordBox(_passwordInput);
        stack.Children.Add(_passwordInput);

        if (_confirmInput is not null)
        {
            stack.Children.Add(CreateLabel("Confirm master password"));
            ConfigurePasswordBox(_confirmInput);
            stack.Children.Add(_confirmInput);
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
        Content = root;

        Loaded += (_, _) => _passwordInput.Focus();
    }

    public string MasterPassword { get; private set; } = string.Empty;

    public static string? ShowForUnlock(Window owner)
    {
        var dialog = new MasterPasswordDialog(
            "Master Password",
            "Master password required",
            "Windows authorized this account. Enter the LockerIt master password to open the local keyring.",
            "Unlock",
            requiresConfirmation: false)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.MasterPassword : null;
    }

    public static string? ShowForSetup(Window owner)
    {
        var dialog = new MasterPasswordDialog(
            "Set Master Password",
            "Set master password",
            "Add a second local factor after Windows authorization. Losing this password requires Recovery Kit import or an already-unlocked source device.",
            "Save",
            requiresConfirmation: true)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.MasterPassword : null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _passwordInput.Password = string.Empty;
        if (_confirmInput is not null)
        {
            _confirmInput.Password = string.Empty;
        }

        if (DialogResult != true)
        {
            MasterPassword = string.Empty;
        }

        base.OnClosed(e);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var masterPassword = _passwordInput.Password;
        if (string.IsNullOrWhiteSpace(masterPassword) || masterPassword.Length < 12)
        {
            _validationText.Text = "Use at least 12 characters for the master password.";
            _passwordInput.Focus();
            return;
        }

        if (_confirmInput is not null && !string.Equals(masterPassword, _confirmInput.Password, StringComparison.Ordinal))
        {
            _validationText.Text = "The master passwords do not match.";
            _confirmInput.Focus();
            return;
        }

        MasterPassword = masterPassword;
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
