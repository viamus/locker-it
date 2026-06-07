using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Lockerit.App;

internal sealed class LockeritMessageDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    private LockeritMessageDialog(string title, string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Border
        {
            Background = LockeritWindowChrome.BrushFrom("#171816"),
            BorderBrush = LockeritWindowChrome.BrushFrom("#34352F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24)
        };

        var stack = new StackPanel();
        root.Child = stack;

        var body = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        body.Children.Add(CreateGlyph(image));
        var textStack = new StackPanel();
        Grid.SetColumn(textStack, 2);
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = LockeritWindowChrome.BrushFrom("#F2EFE7"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        textStack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = LockeritWindowChrome.BrushFrom("#A9A39A"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(textStack);
        stack.Children.Add(body);

        stack.Children.Add(CreateActions(buttons));
        LockeritWindowChrome.Install(this, root, canResize: false);
    }

    public static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new LockeritMessageDialog(title, message, buttons, image)
        {
            Owner = owner
        };

        var accepted = dialog.ShowDialog();
        if (accepted == true || dialog._result != MessageBoxResult.None)
        {
            return dialog._result;
        }

        return buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
    }

    private FrameworkElement CreateActions(MessageBoxButton buttons)
    {
        var actions = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                actions.Children.Add(CreateButton("No", MessageBoxResult.No, isPrimary: false, isCancel: true));
                actions.Children.Add(CreateButton("Yes", MessageBoxResult.Yes, isPrimary: true, isDefault: true));
                break;
            case MessageBoxButton.OKCancel:
                actions.Children.Add(CreateButton("Cancel", MessageBoxResult.Cancel, isPrimary: false, isCancel: true));
                actions.Children.Add(CreateButton("OK", MessageBoxResult.OK, isPrimary: true, isDefault: true));
                break;
            case MessageBoxButton.YesNoCancel:
                actions.Children.Add(CreateButton("Cancel", MessageBoxResult.Cancel, isPrimary: false, isCancel: true));
                actions.Children.Add(CreateButton("No", MessageBoxResult.No, isPrimary: false));
                actions.Children.Add(CreateButton("Yes", MessageBoxResult.Yes, isPrimary: true, isDefault: true));
                break;
            default:
                actions.Children.Add(CreateButton("OK", MessageBoxResult.OK, isPrimary: true, isDefault: true, isCancel: true));
                break;
        }

        return actions;
    }

    private WpfButton CreateButton(
        string text,
        MessageBoxResult result,
        bool isPrimary,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new WpfButton
        {
            Content = text,
            MinWidth = 92,
            MinHeight = 38,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 8, 14, 8),
            Background = isPrimary ? LockeritWindowChrome.BrushFrom("#D97757") : LockeritWindowChrome.BrushFrom("#20211E"),
            BorderBrush = isPrimary ? LockeritWindowChrome.BrushFrom("#D97757") : LockeritWindowChrome.BrushFrom("#34352F"),
            BorderThickness = new Thickness(1),
            Foreground = isPrimary ? LockeritWindowChrome.BrushFrom("#0E0F0D") : LockeritWindowChrome.BrushFrom("#F2EFE7"),
            Cursor = WpfCursors.Hand,
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            IsDefault = isDefault,
            IsCancel = isCancel
        };

        button.Click += (_, _) =>
        {
            _result = result;
            DialogResult = true;
        };

        return button;
    }

    private static FrameworkElement CreateGlyph(MessageBoxImage image)
    {
        var color = image switch
        {
            MessageBoxImage.Error => "#E06A5F",
            MessageBoxImage.Warning => "#E1A34A",
            MessageBoxImage.Question => "#7AA7D9",
            _ => "#65B891"
        };

        return new Border
        {
            Width = 36,
            Height = 36,
            Background = LockeritWindowChrome.BrushFrom("#121311"),
            BorderBrush = LockeritWindowChrome.BrushFrom(color),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new Path
            {
                Width = 17,
                Height = 17,
                Stretch = Stretch.Uniform,
                Stroke = LockeritWindowChrome.BrushFrom(color),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = Geometry.Parse(image switch
                {
                    MessageBoxImage.Error => "M6,6 L18,18 M18,6 L6,18",
                    MessageBoxImage.Warning => "M12,4 L21,20 L3,20 Z M12,9 L12,13 M12,17 L12,17.1",
                    MessageBoxImage.Question => "M9,9 C9.4,6.8 11,5.8 13,6 C15,6.2 16.5,7.6 16.5,9.5 C16.5,12 13,12.2 13,14.5 M13,18 L13,18.1",
                    _ => "M5,13 L10,18 L20,7"
                })
            }
        };
    }
}
