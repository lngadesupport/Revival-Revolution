using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RevivalRevolution;

/// <summary>
/// Branded replacement for system MessageBox dialogs. Keeps the same result semantics
/// while matching the launcher's dark glass surface and red accent.
/// </summary>
public static class LauncherDialog
{
    private static readonly Color Surface = Color.FromRgb(10, 14, 25);
    private static readonly Color Panel = Color.FromRgb(18, 24, 39);
    private static readonly Color Accent = Color.FromRgb(240, 68, 82);
    private static readonly Color Muted = Color.FromRgb(174, 181, 200);

    public static MessageBoxResult Show(Window? owner, string message, string caption,
        MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new Window
        {
            Title = caption,
            Owner = owner,
            Width = 520,
            MinWidth = 420,
            MaxWidth = 680,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 640,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Surface),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            AllowsTransparency = false,
            SnapsToDevicePixels = true
        };

        var frame = new Border
        {
            Background = new SolidColorBrush(Surface),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(1)
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Child = layout;

        var header = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 19, 32)),
            Height = 54,
            Margin = new Thickness(0)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brandMark = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Accent),
            Margin = new Thickness(16, 0, 10, 0),
            Child = new TextBlock
            {
                Text = "R", FontSize = 19, FontWeight = FontWeights.Black,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(brandMark);
        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = "REVIVAL REVOLUTION", FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Accent)
        });
        heading.Children.Add(new TextBlock
        {
            Text = caption, FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        var close = new Button
        {
            Content = "×", Width = 38, Height = 34, Margin = new Thickness(0, 0, 10, 0),
            Background = Brushes.Transparent, Foreground = new SolidColorBrush(Muted),
            BorderThickness = new Thickness(0), FontSize = 22, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => dialog.Close();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, header) && e.ButtonState == MouseButtonState.Pressed) dialog.DragMove();
        };
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var body = new Grid { Margin = new Thickness(22, 22, 22, 18) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = image == MessageBoxImage.Error || image == MessageBoxImage.Hand || image == MessageBoxImage.Stop
            ? "!"
            : image == MessageBoxImage.Warning || image == MessageBoxImage.Exclamation
                ? "!"
                : image == MessageBoxImage.Question ? "?" : "i";
        var glyphColor = image == MessageBoxImage.Error || image == MessageBoxImage.Hand || image == MessageBoxImage.Stop
            ? Color.FromRgb(255, 93, 103)
            : image == MessageBoxImage.Warning || image == MessageBoxImage.Exclamation
                ? Color.FromRgb(255, 190, 110)
                : image == MessageBoxImage.Question
                    ? Color.FromRgb(117, 174, 255)
                    : Color.FromRgb(104, 208, 157);
        body.Children.Add(new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(35, glyphColor.R, glyphColor.G, glyphColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, glyphColor.R, glyphColor.G, glyphColor.B)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = glyph, FontSize = 21, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(glyphColor), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var messageText = new TextBlock
        {
            Text = message, FontSize = 12, LineHeight = 19, Foreground = new SolidColorBrush(Color.FromRgb(220, 224, 235)),
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        };
        var messageScroll = new ScrollViewer
        {
            Content = messageText, MaxHeight = 390,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(15, 0, 0, 0)
        };
        Grid.SetColumn(messageScroll, 1);
        body.Children.Add(messageScroll);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var footer = new Border
        {
            Background = new SolidColorBrush(Panel),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 12, 18, 12)
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        MessageBoxResult result = MessageBoxResult.None;
        void AddAction(string label, MessageBoxResult value, bool primary)
        {
            var button = new Button
            {
                Content = label, MinWidth = 94, Height = 36, Margin = new Thickness(7, 0, 0, 0),
                Padding = new Thickness(15, 0, 15, 0), FontSize = 11, FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand, Foreground = Brushes.White,
                Background = new SolidColorBrush(primary ? Accent : Color.FromRgb(39, 46, 64)),
                BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(255, 105, 116) : Color.FromRgb(76, 85, 108)),
                BorderThickness = new Thickness(1)
            };
            button.Click += (_, _) => { result = value; dialog.Close(); };
            actions.Children.Add(button);
        }

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                AddAction("NÃO", MessageBoxResult.No, false);
                AddAction("SIM", MessageBoxResult.Yes, true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddAction("CANCELAR", MessageBoxResult.Cancel, false);
                AddAction("NÃO", MessageBoxResult.No, false);
                AddAction("SIM", MessageBoxResult.Yes, true);
                break;
            case MessageBoxButton.OKCancel:
                AddAction("CANCELAR", MessageBoxResult.Cancel, false);
                AddAction("OK", MessageBoxResult.OK, true);
                break;
            default:
                AddAction("ENTENDI", MessageBoxResult.OK, true);
                break;
        }
        footer.Child = actions;
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        dialog.Content = frame;
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                result = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
                    ? MessageBoxResult.No : MessageBoxResult.Cancel;
                dialog.Close();
            }
        };
        dialog.ShowDialog();
        return result;
    }

    public static MessageBoxResult Show(string message, string caption,
        MessageBoxButton buttons, MessageBoxImage image) =>
        Show(Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive), message, caption, buttons, image);
}
