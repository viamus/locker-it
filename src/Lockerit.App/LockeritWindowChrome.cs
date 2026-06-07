using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfBinding = System.Windows.Data.Binding;

namespace Lockerit.App;

internal static class LockeritWindowChrome
{
    private const string AppBackground = "#0E0F0D";
    private const string BorderSoft = "#34352F";
    private const string Ink = "#F2EFE7";
    private const string Muted = "#A9A39A";
    private const string Primary = "#D97757";
    private const string PrimaryHover = "#251C17";

    public static void Install(Window window, UIElement body, bool canResize)
    {
        if (ReferenceEquals(window.Content, body))
        {
            window.Content = null;
        }

        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = false;
        window.ResizeMode = canResize ? ResizeMode.CanResize : ResizeMode.NoResize;
        window.Background = BrushFrom(AppBackground);
        window.Foreground = BrushFrom(Ink);
        window.FontFamily = new WpfFontFamily("Segoe UI");
        window.SnapsToDevicePixels = true;
        WindowChrome.SetWindowChrome(
            window,
            new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = canResize ? new Thickness(6) : new Thickness(0),
                UseAeroCaptionButtons = false
            });

        var frame = new Border
        {
            Background = BrushFrom(AppBackground),
            BorderBrush = BrushFrom(BorderSoft),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true
        };

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Children.Add(CreateTitleBar(window, showWindowControls: canResize));
        Grid.SetRow(body, 1);
        shell.Children.Add(body);

        frame.Child = shell;
        window.Content = frame;
    }

    private static Grid CreateTitleBar(Window window, bool showWindowControls)
    {
        var titleBar = new Grid
        {
            Height = 42,
            Background = BrushFrom(AppBackground),
            Cursor = WpfCursors.SizeAll
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (e.ClickCount == 2 && showWindowControls)
            {
                ToggleMaximize(window);
                return;
            }

            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if Windows has already ended the mouse gesture.
            }
        };

        var brand = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            IsHitTestVisible = false
        };
        brand.Children.Add(CreateMark());
        brand.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(window.Title) ? "Lockerit" : window.Title,
            Foreground = BrushFrom(Ink),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        titleBar.Children.Add(brand);

        var controls = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        Grid.SetColumn(controls, 2);

        if (showWindowControls)
        {
            controls.Children.Add(CreateTitleButton("Minimize", "M5,12 L19,12", BrushFrom(Ink), (_, _) => window.WindowState = WindowState.Minimized));

            controls.Children.Add(CreateTitleButton("Maximize", "M6,6 L18,6 L18,18 L6,18 Z", BrushFrom(Ink), (_, _) => ToggleMaximize(window)));
        }

        controls.Children.Add(CreateTitleButton("Close", "M7,7 L17,17 M17,7 L7,17", BrushFrom(Primary), (_, _) => window.Close(), isClose: true));
        titleBar.Children.Add(controls);

        return titleBar;
    }

    private static FrameworkElement CreateMark()
    {
        return new Border
        {
            Width = 24,
            Height = 24,
            Background = BrushFrom(PrimaryHover),
            BorderBrush = BrushFrom("#6B3B2B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = new Path
            {
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                Fill = BrushFrom(Primary),
                Data = Geometry.Parse("M12,2 C8.7,2 6,4.7 6,8 L6,10 L5,10 C3.9,10 3,10.9 3,12 L3,21 C3,22.1 3.9,23 5,23 L19,23 C20.1,23 21,22.1 21,21 L21,12 C21,10.9 20.1,10 19,10 L18,10 L18,8 C18,4.7 15.3,2 12,2 Z M8,10 L8,8 C8,5.8 9.8,4 12,4 C14.2,4 16,5.8 16,8 L16,10 Z")
            }
        };
    }

    private static WpfButton CreateTitleButton(
        string tooltip,
        string geometry,
        WpfBrush foreground,
        RoutedEventHandler click,
        bool isClose = false)
    {
        var button = new WpfButton
        {
            Width = 42,
            Height = 42,
            MinWidth = 42,
            MinHeight = 42,
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = foreground,
            Cursor = WpfCursors.Hand,
            ToolTip = tooltip,
            Template = CreateTitleButtonTemplate(isClose)
        };

        var icon = new Path
        {
            Width = 12,
            Height = 12,
            Data = Geometry.Parse(geometry),
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        icon.SetBinding(Shape.StrokeProperty, new WpfBinding(nameof(WpfButton.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(WpfButton), 1)
        });

        button.Content = icon;
        button.Click += click;
        return button;
    }

    private static ControlTemplate CreateTitleButtonTemplate(bool isClose)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.BackgroundProperty, WpfBrushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(WpfButton))
        {
            VisualTree = border
        };

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, BrushFrom(isClose ? PrimaryHover : "#20211E"), "Chrome"));
        template.Triggers.Add(hover);

        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
        template.Triggers.Add(disabled);

        return template;
    }

    private static void ToggleMaximize(Window window)
    {
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    internal static SolidColorBrush BrushFrom(string color)
    {
        return new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color));
    }
}
