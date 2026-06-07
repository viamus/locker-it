using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Lockerit.App;

internal sealed class RecoveryCodesDialog : Window
{
    private readonly IReadOnlyList<string> _codes;

    private RecoveryCodesDialog(IReadOnlyList<string> codes)
    {
        _codes = codes;

        Title = "Recovery Codes";
        Width = 520;
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
            Text = "Save your recovery codes",
            Foreground = BrushFrom("#F2EFE7"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Each code can unlock this vault once if the authenticator app is unavailable. Store them separately from the vault database and Recovery Kit.",
            Foreground = BrushFrom("#A9A39A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18)
        });

        var codeGrid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 16)
        };

        foreach (var code in codes)
        {
            codeGrid.Children.Add(new Border
            {
                Background = BrushFrom("#121311"),
                BorderBrush = BrushFrom("#34352F"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock
                {
                    Text = code,
                    Foreground = BrushFrom("#F2EFE7"),
                    FontFamily = new WpfFontFamily("Consolas"),
                    FontSize = 14,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                }
            });
        }

        stack.Children.Add(codeGrid);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var copyButton = CreateButton("Copy codes", BrushFrom("#20211E"), BrushFrom("#34352F"), BrushFrom("#F2EFE7"));
        copyButton.Margin = new Thickness(0, 0, 8, 0);
        copyButton.Click += (_, _) => System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, _codes));
        actions.Children.Add(copyButton);

        var doneButton = CreateButton("Done", BrushFrom("#D97757"), BrushFrom("#D97757"), BrushFrom("#0E0F0D"));
        doneButton.FontWeight = FontWeights.SemiBold;
        doneButton.Click += (_, _) => DialogResult = true;
        actions.Children.Add(doneButton);

        stack.Children.Add(actions);
        LockeritWindowChrome.Install(this, root, canResize: false);
    }

    public static void ShowCodes(Window owner, IReadOnlyList<string> codes)
    {
        new RecoveryCodesDialog(codes)
        {
            Owner = owner
        }.ShowDialog();
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
