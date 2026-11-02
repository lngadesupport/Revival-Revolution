using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public sealed class LauncherSettingsWindow : Window
{
    private readonly TextBox _playerName;
    private readonly TextBox _playerTag;
    private readonly CheckBox _startupUpdates;
    private readonly CheckBox _confirmLaunch;
    private readonly CheckBox _persistLogs;

    public LauncherSettings Result { get; private set; }

    public LauncherSettingsWindow(LauncherSettings current, string gameFolder, string settingsPath, string logDirectory)
    {
        Result = new LauncherSettings
        {
            PlayerName = current.PlayerName,
            PlayerTag = current.PlayerTag,
            CheckUpdatesOnStartup = current.CheckUpdatesOnStartup,
            ConfirmBeforeLaunch = current.ConfirmBeforeLaunch,
            PersistLogs = current.PersistLogs
        };

        Title = "Configurações — Revival Revolution";
        Width = 590;
        Height = 700;
        MinWidth = 520;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(12, 14, 25));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock
        {
            Text = "CONFIGURAÇÕES",
            FontSize = 27,
            FontWeight = FontWeights.Black,
            Foreground = Brushes.White
        });
        root.Children.Add(new TextBlock
        {
            Text = "Personalize seu perfil e o comportamento do launcher.",
            Margin = new Thickness(0, 2, 0, 18),
            Foreground = new SolidColorBrush(Color.FromRgb(190, 192, 205))
        });

        AddSectionTitle(root, "PERFIL DO JOGADOR");
        _playerName = AddTextField(root, "Nome do jogador", current.PlayerName, 24,
            "Nome de exibição salvo no perfil local do launcher.");
        _playerTag = AddTextField(root, "Tag / clã (opcional)", current.PlayerTag, 12,
            "Identificador curto para personalização futura do perfil.");

        root.Children.Add(new TextBlock
        {
            Text = "Esses dados ficam salvos no launcher. Ainda não são enviados nem sincronizados com o jogo.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 12),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 120))
        });

        AddSectionTitle(root, "PREFERÊNCIAS");
        _startupUpdates = AddOption(root, "Verificar atualizações ao abrir",
            "Consulta a Release oficial sem bloquear a inicialização se a rede falhar.", current.CheckUpdatesOnStartup);
        _confirmLaunch = AddOption(root, "Confirmar antes de iniciar o jogo",
            "Exibe uma confirmação antes de executar o modo offline.", current.ConfirmBeforeLaunch);
        _persistLogs = AddOption(root, "Salvar logs no computador",
            "Mantém um histórico local para facilitar diagnósticos.", current.PersistLogs);

        AddSectionTitle(root, "INSTALAÇÃO DO JOGO");
        root.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(gameFolder) ? "Nenhuma pasta selecionada." : gameFolder,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 205, 216))
        });
        root.Children.Add(new TextBlock
        {
            Text = "Configurações: " + settingsPath + Environment.NewLine + "Logs: " + logDirectory,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(145, 149, 165))
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = MakeButton("CANCELAR", false);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = MakeButton("SALVAR CONFIGURAÇÕES", true);
        save.Margin = new Thickness(10, 0, 0, 0);
        save.Click += (_, _) =>
        {
            var playerName = _playerName.Text.Trim();
            var playerTag = _playerTag.Text.Trim();
            if (playerName.Length > 24 || playerTag.Length > 12)
            {
                MessageBox.Show(this, "O nome aceita até 24 caracteres e a tag até 12.",
                    "Perfil inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new LauncherSettings
            {
                PlayerName = playerName,
                PlayerTag = playerTag,
                CheckUpdatesOnStartup = _startupUpdates.IsChecked == true,
                ConfirmBeforeLaunch = _confirmLaunch.IsChecked == true,
                PersistLogs = _persistLogs.IsChecked == true
            };
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void AddSectionTitle(Panel parent, string title)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            Margin = new Thickness(0, 12, 0, 6),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 118))
        });
    }

    private static TextBox AddTextField(Panel parent, string title, string value, int maxLength, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 5)
        });
        var input = new TextBox
        {
            Text = value ?? "",
            MaxLength = maxLength,
            Padding = new Thickness(9, 7, 9, 7),
            Background = new SolidColorBrush(Color.FromRgb(25, 28, 43)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 80, 100)),
            CaretBrush = Brushes.White,
            FontSize = 13
        };
        panel.Children.Add(input);
        panel.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 177, 192))
        });
        parent.Children.Add(panel);
        return input;
    }

    private static CheckBox AddOption(Panel parent, string title, string description, bool isChecked)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 7, 0, 7) };
        var check = new CheckBox
        {
            Content = title,
            IsChecked = isChecked,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(check);
        panel.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(24, 3, 0, 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 177, 192))
        });
        parent.Children.Add(panel);
        return check;
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(16, 10, 16, 10),
        Background = new SolidColorBrush(primary ? Color.FromRgb(180, 35, 49) : Color.FromRgb(40, 44, 60)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(100, 105, 125)),
        BorderThickness = new Thickness(1),
        FontWeight = FontWeights.Bold,
        Cursor = System.Windows.Input.Cursors.Hand
    };
}
