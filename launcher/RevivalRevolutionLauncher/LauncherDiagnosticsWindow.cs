using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public sealed class LauncherDiagnosticsWindow : Window
{
    private static readonly Color BackgroundColor = Color.FromRgb(9, 11, 17);
    private static readonly Color SurfaceColor = Color.FromRgb(18, 22, 31);
    private static readonly Color RaisedColor = Color.FromRgb(27, 33, 44);
    private static readonly Color BorderColor = Color.FromRgb(55, 65, 80);
    private static readonly Color MutedColor = Color.FromRgb(158, 169, 185);
    private static readonly Color AccentColor = Color.FromRgb(224, 65, 80);

    private readonly IReadOnlyList<DiagnosticItem> _items;
    private readonly string _logDirectory;
    private readonly StackPanel _results = new();
    private readonly TextBox _search = new();
    private string _filter = "all";

    public LauncherDiagnosticsWindow(IReadOnlyList<DiagnosticItem> items, string logDirectory)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _logDirectory = logDirectory;

        Title = "Diagnósticos — Revival Revolution";
        Width = 900;
        Height = 760;
        MinWidth = 680;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush(BackgroundColor);
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var root = new Grid { Margin = new Thickness(26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "CENTRAL DE DIAGNÓSTICOS",
            Foreground = Brush(AccentColor),
            FontSize = 9,
            FontWeight = FontWeights.Bold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Integridade da instalação",
            FontSize = 27,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 3)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Verificações locais dos componentes necessários ao launcher e aos modos disponíveis.",
            Foreground = Brush(MutedColor),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(titleStack);

        var close = MakeButton("FECHAR", false);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);

        var summary = BuildSummary();
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        var body = new DockPanel { Margin = new Thickness(0, 18, 0, 16), LastChildFill = true };
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.Height = 38;
        _search.Padding = new Thickness(11, 8, 11, 8);
        _search.Background = Brush(RaisedColor);
        _search.Foreground = Brushes.White;
        _search.BorderBrush = Brush(BorderColor);
        _search.BorderThickness = new Thickness(1);
        _search.ToolTip = "Filtrar verificações pelo nome ou pelo caminho";
        _search.TextChanged += (_, _) => RenderResults();
        _search.Margin = new Thickness(0, 0, 12, 0);
        toolbar.Children.Add(_search);

        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(FilterButton("TODAS", "all"));
        filters.Children.Add(FilterButton("ESSENCIAIS", "required"));
        filters.Children.Add(FilterButton("OPCIONAIS", "optional"));
        Grid.SetColumn(filters, 1);
        toolbar.Children.Add(filters);
        DockPanel.SetDock(toolbar, Dock.Top);
        body.Children.Add(toolbar);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Content = _results
        };
        body.Children.Add(scroll);
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = new TextBlock
        {
            Text = "Opcionais não bloqueiam o modo offline. Caminhos pessoais são ocultados ao copiar o relatório.",
            Foreground = Brush(MutedColor),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var openLogs = MakeButton("ABRIR LOGS", false);
        openLogs.Margin = new Thickness(0, 0, 8, 0);
        openLogs.Click += (_, _) => OpenLogs();
        var copy = MakeButton("COPIAR RELATÓRIO", true);
        copy.Click += (_, _) => CopyReport();
        actions.Children.Add(openLogs);
        actions.Children.Add(copy);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
        RenderResults();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F &&
                     (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
            }
        };
    }

    private UIElement BuildSummary()
    {
        var required = _items.Where(x => !x.Optional).ToArray();
        var passed = required.Count(x => x.Passed);
        var failed = required.Length - passed;
        var optionalAvailable = _items.Count(x => x.Optional &&
            !x.Detail.StartsWith("Ausente;", StringComparison.OrdinalIgnoreCase) &&
            !x.Detail.StartsWith("Não instalado", StringComparison.OrdinalIgnoreCase));
        var grid = new Grid();
        for (var i = 0; i < 3; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddSummaryCard(grid, 0, "ESSENCIAIS APROVADOS", $"{passed} / {required.Length}",
            failed == 0 ? Color.FromRgb(113, 218, 151) : Color.FromRgb(255, 126, 135));
        AddSummaryCard(grid, 1, "PENDÊNCIAS ESSENCIAIS", failed.ToString(),
            failed == 0 ? Color.FromRgb(113, 218, 151) : Color.FromRgb(255, 126, 135));
        AddSummaryCard(grid, 2, "COMPONENTES OPCIONAIS", $"{optionalAvailable} / {_items.Count(x => x.Optional)}",
            Color.FromRgb(214, 177, 112));
        return grid;
    }

    private static void AddSummaryCard(Grid parent, int column, string label, string value, Color valueColor)
    {
        var card = new Border
        {
            Background = Brush(SurfaceColor),
            BorderBrush = Brush(BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15, 12, 15, 12),
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(MutedColor)
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(valueColor),
            Margin = new Thickness(0, 5, 0, 0)
        });
        card.Child = stack;
        Grid.SetColumn(card, column);
        parent.Children.Add(card);
    }

    private Button FilterButton(string label, string key)
    {
        var button = MakeButton(label, false);
        button.FontSize = 9;
        button.Padding = new Thickness(10, 9, 10, 9);
        button.Margin = new Thickness(0, 0, 5, 0);
        button.Click += (_, _) =>
        {
            _filter = key;
            RenderResults();
        };
        return button;
    }

    private void RenderResults()
    {
        _results.Children.Clear();
        var query = _search.Text.Trim();
        var visible = _items.Where(item =>
                (_filter == "all" || (_filter == "optional" ? item.Optional : !item.Optional)) &&
                (query.Length == 0 ||
                 item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 item.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (visible.Length == 0)
        {
            _results.Children.Add(new TextBlock
            {
                Text = "Nenhuma verificação corresponde ao filtro.",
                Foreground = Brush(MutedColor),
                Margin = new Thickness(4, 18, 4, 18)
            });
            return;
        }

        foreach (var item in visible)
            _results.Children.Add(BuildResultCard(item));
    }

    private static UIElement BuildResultCard(DiagnosticItem item)
    {
        var optionalMissing = item.Optional &&
            (item.Detail.StartsWith("Ausente;", StringComparison.OrdinalIgnoreCase) ||
             item.Detail.StartsWith("Não instalado", StringComparison.OrdinalIgnoreCase));
        var label = item.Optional
            ? !item.Passed ? "AVISO" : optionalMissing ? "OPCIONAL AUSENTE" : "OPCIONAL"
            : item.Passed ? "APROVADO" : "ATENÇÃO";
        var color = item.Optional
            ? Color.FromRgb(214, 177, 112)
            : item.Passed ? Color.FromRgb(113, 218, 151) : Color.FromRgb(255, 126, 135);

        var card = new Border
        {
            Background = Brush(SurfaceColor),
            BorderBrush = Brush(BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel();
        labels.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });
        labels.Children.Add(new TextBlock
        {
            Text = item.Detail,
            FontSize = 9,
            Foreground = Brush(MutedColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 12, 0)
        });
        layout.Children.Add(labels);

        var badge = new Border
        {
            Background = Brush(Color.FromArgb(35, color.R, color.G, color.B)),
            BorderBrush = Brush(Color.FromArgb(125, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(color)
            }
        };
        Grid.SetColumn(badge, 1);
        layout.Children.Add(badge);
        card.Child = layout;
        return card;
    }

    private void CopyReport()
    {
        var required = _items.Where(item => !item.Optional).ToArray();
        var optional = _items.Where(item => item.Optional).ToArray();
        var version = typeof(LauncherDiagnosticsWindow).Assembly.GetName().Version;
        var lines = new List<string>
        {
            "Revival Revolution — Relatório de diagnóstico",
            $"Versão: {(version is null ? "desconhecida" : $"v{version.Major}.{version.Minor}.{version.Build}")}",
            $"Data local: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Verificações essenciais: {required.Count(item => item.Passed)}/{required.Length} aprovadas",
            $"Componentes opcionais disponíveis: {optional.Count(item => item.Passed)}/{optional.Length}",
            "Privacidade: caminhos sob o perfil do Windows são substituídos por %USERPROFILE%.",
            ""
        };
        lines.AddRange(_items.Select(item =>
        {
            var severity = item.Optional
                ? item.Passed ? "OPCIONAL" : "AVISO"
                : item.Passed ? "APROVADO" : "FALHA";
            return $"[{severity}] {item.Name}{Environment.NewLine}  {SanitizeReportDetail(item.Detail)}";
        }));
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            MessageBox.Show(this, "Relatório copiado para a área de transferência.",
                "Diagnóstico", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível copiar o relatório: " + ex.Message,
                "Diagnóstico", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SanitizeReportDetail(string detail)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile)) return detail;
        return detail.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenLogs()
    {
        try
        {
            if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);
            Process.Start(new ProcessStartInfo { FileName = _logDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir a pasta de logs: " + ex.Message,
                "Diagnóstico", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(14, 10, 14, 10),
        Background = Brush(primary ? AccentColor : RaisedColor),
        Foreground = Brushes.White,
        BorderBrush = Brush(primary ? Color.FromRgb(246, 111, 121) : BorderColor),
        BorderThickness = new Thickness(1),
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush Brush(Color color) => new(color);
}
