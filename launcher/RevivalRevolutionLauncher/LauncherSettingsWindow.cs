using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public sealed class LauncherSettingsWindow : Window
{
    private static readonly Color BackgroundColor = Color.FromRgb(10, 12, 20);
    private static readonly Color SurfaceColor = Color.FromRgb(20, 23, 35);
    private static readonly Color SurfaceRaisedColor = Color.FromRgb(27, 30, 44);
    private static readonly Color BorderColor = Color.FromRgb(54, 59, 77);
    private static readonly Color MutedColor = Color.FromRgb(157, 163, 181);
    private static readonly Color AccentColor = Color.FromRgb(218, 48, 62);

    private readonly ToggleButton _confirmLaunch = null!;
    private readonly ToggleButton _persistLogs = null!;
    private readonly CheckBox _customRadminIpEnabled = null!;
    private readonly TextBox _customRadminIp = null!;

    public LauncherSettings Result { get; private set; }

    public LauncherSettingsWindow(LauncherSettings current, string gameFolder, string settingsPath, string logDirectory)
    {
        Result = CopySettings(current);

        Title = "Configurações — Revival Revolution";
        Width = 790;
        Height = 820;
        MinWidth = 680;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Background = Brush(BackgroundColor);
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(28, 24, 28, 22), LastChildFill = true };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var back = new Button
        {
            Content = "←",
            Width = 42,
            Height = 42,
            FontSize = 19,
            Padding = new Thickness(0),
            Background = Brush(SurfaceRaisedColor),
            BorderBrush = Brush(BorderColor),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top
        };
        back.Click += (_, _) => { DialogResult = false; Close(); };
        header.Children.Add(back);

        var heading = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        heading.Children.Add(new TextBlock
        {
            Text = "SETTINGS",
            FontSize = 27,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Preferências e suporte em um só lugar.",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 12,
            Foreground = Brush(MutedColor)
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var reset = MakeButton("RESTAURAR PADRÕES", false);
        reset.Click += (_, _) =>
        {
            _confirmLaunch.IsChecked = false;
            _persistLogs.IsChecked = true;
            _customRadminIpEnabled.IsChecked = false;
            _customRadminIp.Text = "";
        };
        var cancel = MakeButton("CANCELAR", false);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = MakeButton("SALVAR ALTERAÇÕES", true);
        save.Margin = new Thickness(10, 0, 0, 0);
        save.Click += (_, _) =>
        {
            Result = CopySettings(current);
            Result.ConfirmBeforeLaunch = _confirmLaunch.IsChecked == true;
            Result.PersistLogs = _persistLogs.IsChecked == true;
            Result.UseCustomRadminIp = _customRadminIpEnabled.IsChecked == true;
            Result.CustomRadminIp = _customRadminIp.Text.Trim();
            if (Result.UseCustomRadminIp && !IsValidRadminIp(Result.CustomRadminIp))
            {
                MessageBox.Show(this, "Informe um IPv4 da faixa 26.x.x.x.", "IP Radmin inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        };
        footer.Children.Add(reset);
        cancel.Margin = new Thickness(10, 0, 0, 0);
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var generalTabContent = new StackPanel();
        var networkTabContent = new StackPanel();
        var dataTabContent = new StackPanel();
        var supportTabContent = new StackPanel();

        var accountCard = MakeCard();
        var account = CardContent(accountCard);
        AddCardHeading(account, "ACCOUNT", "Privacidade e dados locais");
        var accountRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var accountIcon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
            Background = Brush(Color.FromRgb(48, 34, 48)),
            Child = new TextBlock { Text = "●", Foreground = Brush(Color.FromRgb(255, 111, 122)),
                FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center }
        };
        accountRow.Children.Add(accountIcon);
        var accountLabels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        accountLabels.Children.Add(new TextBlock
        {
            Text = "Conta local • sem login obrigatório",
            FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
        });
        accountLabels.Children.Add(new TextBlock
        {
            Text = "Suas preferências ficam neste computador e não são sincronizadas com uma conta online.",
            FontSize = 10, Foreground = Brush(MutedColor), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 12, 0)
        });
        Grid.SetColumn(accountLabels, 1);
        accountRow.Children.Add(accountLabels);
        var openData = MakeButton("ABRIR DADOS", false);
        openData.Padding = new Thickness(11, 8, 11, 8);
        openData.VerticalAlignment = VerticalAlignment.Center;
        openData.Click += (_, _) => OpenFolder(Path.GetDirectoryName(settingsPath) ?? settingsPath);
        Grid.SetColumn(openData, 2);
        accountRow.Children.Add(openData);
        account.Children.Add(accountRow);
        dataTabContent.Children.Add(accountCard);

        var appCard = MakeCard();
        var app = CardContent(appCard);
        AddCardHeading(app, "APP SETTINGS", "Comportamento do launcher");
        _confirmLaunch = AddSwitchRow(app, "Confirmar antes de iniciar o jogo",
            "Solicita confirmação antes de abrir o modo offline.", current.ConfirmBeforeLaunch);
        AddRowDivider(app);
        _persistLogs = AddSwitchRow(app, "Salvar logs neste computador",
            "Mantém registros locais para facilitar diagnósticos.", current.PersistLogs);
        generalTabContent.Children.Add(appCard);

        var networkCard = MakeCard();
        var network = CardContent(networkCard);
        AddCardHeading(network, "RADMIN VPN", "Conexão avançada com a rede privada");
        network.Children.Add(new TextBlock
        {
            Text = "Por padrão, o launcher usa o endpoint Radmin configurado. Altere somente se o anfitrião fornecer outro IPv4 da faixa 26.x.x.x.",
            FontSize = 10, Foreground = Brush(MutedColor), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 12)
        });
        _customRadminIpEnabled = new CheckBox
        {
            Content = "Desbloquear endereço manual do servidor",
            IsChecked = current.UseCustomRadminIp, Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 8)
        };
        _customRadminIp = new TextBox
        {
            Text = current.CustomRadminIp, IsEnabled = current.UseCustomRadminIp,
            Height = 38, Padding = new Thickness(10, 8, 10, 8),
            ToolTip = "IPv4 da rede Radmin: 26.x.x.x"
        };
        _customRadminIpEnabled.Checked += (_, _) => _customRadminIp.IsEnabled = true;
        _customRadminIpEnabled.Unchecked += (_, _) => _customRadminIp.IsEnabled = false;
        network.Children.Add(_customRadminIpEnabled);
        network.Children.Add(_customRadminIp);
        networkTabContent.Children.Add(networkCard);

        var supportCard = MakeCard();
        var support = CardContent(supportCard);
        AddCardHeading(support, "SUPPORT", "Arquivos e ajuda");
        AddInfoRow(support, "Pasta do jogo", string.IsNullOrWhiteSpace(gameFolder)
            ? "Nenhuma pasta selecionada." : gameFolder);
        AddRowDivider(support);
        var settingsRow = AddInfoRow(support, "Arquivo de configurações", settingsPath);
        var openSettings = MakeButton("ABRIR", false);
        openSettings.Padding = new Thickness(12, 7, 12, 7);
        openSettings.Margin = new Thickness(12, 0, 0, 0);
        openSettings.VerticalAlignment = VerticalAlignment.Center;
        openSettings.Click += (_, _) => OpenFolder(Path.GetDirectoryName(settingsPath) ?? settingsPath);
        Grid.SetColumn(openSettings, 2);
        settingsRow.Children.Add(openSettings);
        AddRowDivider(support);
        var logsRow = AddInfoRow(support, "Logs do launcher", logDirectory);
        var openLogs = MakeButton("ABRIR", false);
        openLogs.Padding = new Thickness(12, 7, 12, 7);
        openLogs.Margin = new Thickness(12, 0, 0, 0);
        openLogs.VerticalAlignment = VerticalAlignment.Center;
        openLogs.Click += (_, _) => OpenFolder(logDirectory);
        Grid.SetColumn(openLogs, 2);
        logsRow.Children.Add(openLogs);
        AddRowDivider(support);
        AddInfoRow(support, "Sobre", "Revival Revolution Launcher — interface comunitária para Metal Revolution.");
        var version = typeof(LauncherSettingsWindow).Assembly.GetName().Version;
        AddInfoRow(support, "Versão", version is null
            ? "Versão não identificada" : $"v{version.Major}.{version.Minor}.{version.Build}");
        AddRowDivider(support);
        AddCredit(support, "Criador original do projeto comunitário", "@NC_KuroRaiden",
            "https://x.com/NC_KuroRaiden");
        AddCredit(support, "Launcher • desenvolvimento e manutenção", "@lngadesupport",
            "https://github.com/lngadesupport");
        support.Children.Add(new TextBlock
        {
            Text = "Projeto comunitário independente. Os créditos do criador original são mantidos; este launcher não reivindica a autoria de Metal Revolution, desenvolvido pela NEXT Studios.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
            FontSize = 10,
            Foreground = Brush(MutedColor)
        });
        supportTabContent.Children.Add(supportCard);

        var tabs = new TabControl
        {
            Background = Brush(BackgroundColor),
            BorderBrush = Brush(BorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            ItemContainerStyle = CreateTabItemStyle()
        };
        tabs.Items.Add(new TabItem
        {
            Header = "GERAL",
            Content = CreateTabScrollViewer(generalTabContent)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "REDE",
            Content = CreateTabScrollViewer(networkTabContent)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "DADOS",
            Content = CreateTabScrollViewer(dataTabContent)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "SUPORTE",
            Content = CreateTabScrollViewer(supportTabContent)
        });
        root.Children.Add(tabs);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.S &&
                     (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        };
    }

    private static bool IsValidRadminIp(string value) =>
        IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] == 26;

    private static LauncherSettings CopySettings(LauncherSettings current) => new()
    {
        PlayerName = current.PlayerName,
        PlayerTag = current.PlayerTag,
        ConfirmBeforeLaunch = current.ConfirmBeforeLaunch,
        PersistLogs = current.PersistLogs,
        UseCustomRadminIp = current.UseCustomRadminIp,
        CustomRadminIp = current.CustomRadminIp
    };

    private static ScrollViewer CreateTabScrollViewer(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Background = Brushes.Transparent,
        CanContentScroll = true
    };

    private static Style CreateTabItemStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(SurfaceRaisedColor)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(BorderColor)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 9, 14, 9)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 8, 10)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 10d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetBinding(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(14, 9, 14, 9));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(UIElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(UIElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush(AccentColor)));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(Color.FromRgb(245, 104, 114))));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Border MakeCard()
    {
        var card = new Border
        {
            SnapsToDevicePixels = true,
            Background = Brush(SurfaceColor),
            BorderBrush = Brush(BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 14)
        };
        card.Child = new StackPanel();
        return card;
    }

    private static StackPanel CardContent(Border card) => (StackPanel)card.Child;

    private static void AddCardHeading(Panel parent, string title, string subtitle)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Color.FromRgb(255, 111, 122))
        });
        parent.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 3, 0, 0)
        });
    }

    private static ToggleButton AddSwitchRow(Panel parent, string title, string description, bool isChecked)
    {
        var row = new Grid { Margin = new Thickness(0, 11, 0, 11) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });
        labels.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 10,
            Foreground = Brush(MutedColor),
            Margin = new Thickness(0, 3, 16, 0),
            TextWrapping = TextWrapping.Wrap
        });
        row.Children.Add(labels);

        var toggle = new ToggleButton
        {
            Width = 46,
            Height = 25,
            IsChecked = isChecked,
            Focusable = true,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Template = CreateSwitchTemplate()
        };
        System.Windows.Automation.AutomationProperties.SetName(toggle, title);
        System.Windows.Automation.AutomationProperties.SetHelpText(toggle, description);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        parent.Children.Add(row);
        return toggle;
    }

    private static ControlTemplate CreateSwitchTemplate()
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        var track = new FrameworkElementFactory(typeof(Border));
        track.Name = "Track";
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        track.SetValue(Border.BackgroundProperty, Brush(Color.FromRgb(67, 72, 88)));
        track.SetValue(Border.BorderBrushProperty, Brush(Color.FromRgb(94, 100, 117)));
        track.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        track.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        grid.AppendChild(track);

        var knob = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        knob.Name = "Knob";
        knob.SetValue(FrameworkElement.WidthProperty, 17d);
        knob.SetValue(FrameworkElement.HeightProperty, 17d);
        knob.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
        knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        knob.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);
        knob.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(knob);

        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = grid };
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brush(AccentColor), "Track"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(Color.FromRgb(245, 104, 114)), "Track"));
        on.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob"));
        template.Triggers.Add(on);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        return template;
    }

    private static Grid AddInfoRow(Panel parent, string title, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = value,
            FontSize = 10,
            MaxWidth = 420,
            Foreground = Brush(MutedColor),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        parent.Children.Add(row);
        return row;
    }

    private static void AddRowDivider(Panel parent)
    {
        parent.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(BorderColor),
            Opacity = 0.65
        });
    }

    private static void AddCredit(Panel parent, string role, string account, string url)
    {
        var row = new StackPanel { Margin = new Thickness(0, 8, 0, 2) };
        row.Children.Add(new TextBlock
        {
            Text = role,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(MutedColor)
        });
        var link = new Button
        {
            Content = account + "  ›",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0, 4, 8, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush(Color.FromRgb(255, 111, 122)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        link.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível abrir o link: " + ex.Message,
                    "Abrir perfil", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        row.Children.Add(link);
        parent.Children.Add(row);
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(17, 11, 17, 11),
        Background = Brush(primary ? AccentColor : SurfaceRaisedColor),
        Foreground = Brushes.White,
        BorderBrush = Brush(primary ? Color.FromRgb(245, 104, 114) : BorderColor),
        BorderThickness = new Thickness(1),
        FontWeight = FontWeights.Bold,
        FontSize = 11,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush Brush(Color color) => new(color);

    private void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir a pasta: " + ex.Message,
                "Abrir pasta", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
