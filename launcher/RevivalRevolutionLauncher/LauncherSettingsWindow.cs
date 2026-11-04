using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevivalRevolution.Services;

namespace RevivalRevolution;

public sealed class LauncherSettingsWindow : Window
{
    private TextBox _playerName = null!;
    private TextBox _playerTag = null!;
    private CheckBox _startupUpdates = null!;
    private CheckBox _confirmLaunch = null!;
    private CheckBox _persistLogs = null!;

    private ComboBox _fpsLimit = null!;
    private ComboBox _graphicsPreset = null!;
    private ComboBox _resolution = null!;
    private CheckBox _fullscreen = null!;
    private CheckBox _vSync = null!;
    private CheckBox _showFps = null!;

    private Slider _masterVolume = null!;
    private Slider _musicVolume = null!;
    private Slider _effectsVolume = null!;
    private CheckBox _muteAudio = null!;

    private CheckBox _subtitlesEnabled = null!;
    private ComboBox _subtitleSize = null!;
    private ComboBox _subtitleColor = null!;
    private ComboBox _subtitleBackground = null!;
    private ComboBox _subtitlePosition = null!;

    private Slider _leftDeadZone = null!;
    private Slider _rightDeadZone = null!;
    private Slider _triggerDeadZone = null!;
    private Slider _horizontalSensitivity = null!;
    private Slider _verticalSensitivity = null!;
    private ComboBox _leftCurve = null!;
    private ComboBox _rightCurve = null!;
    private CheckBox _invertHorizontal = null!;
    private CheckBox _invertVertical = null!;
    private CheckBox _controllerVibration = null!;

    public LauncherSettings Result { get; private set; }

    public LauncherSettingsWindow(LauncherSettings current, string gameFolder, string settingsPath, string logDirectory)
    {
        Result = Copy(current);

        Title = "Configurações — Revival Revolution";
        Width = 850;
        Height = 790;
        MinWidth = 760;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(12, 14, 25));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "CONFIGURAÇÕES",
            FontSize = 27,
            FontWeight = FontWeights.Black,
            Foreground = Brushes.White
        });
        header.Children.Add(new TextBlock
        {
            Text = "Preferências locais do launcher e opções preparadas para integração com o jogo.",
            Margin = new Thickness(0, 2, 0, 14),
            Foreground = new SolidColorBrush(Color.FromRgb(190, 192, 205))
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var reset = MakeButton("RESTAURAR PADRÕES", false);
        reset.Click += (_, _) =>
        {
            var answer = MessageBox.Show(this,
                "Restaurar todas as preferências desta janela para os valores padrão?",
                "Restaurar padrões", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                var defaults = new LauncherSettings();
                ApplyValues(defaults);
            }
        };
        var cancel = MakeButton("CANCELAR", false);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = MakeButton("SALVAR CONFIGURAÇÕES", true);
        save.Margin = new Thickness(8, 0, 0, 0);
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

            Result = ReadValues(playerName, playerTag);
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(reset);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var tabs = new TabControl
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 21, 34)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(65, 70, 91)),
            BorderThickness = new Thickness(1)
        };

        _playerName = null!;
        _playerTag = null!;
        _startupUpdates = null!;
        _confirmLaunch = null!;
        _persistLogs = null!;
        _fpsLimit = null!;
        _graphicsPreset = null!;
        _resolution = null!;
        _fullscreen = null!;
        _vSync = null!;
        _showFps = null!;
        _masterVolume = null!;
        _musicVolume = null!;
        _effectsVolume = null!;
        _muteAudio = null!;
        _subtitlesEnabled = null!;
        _subtitleSize = null!;
        _subtitleColor = null!;
        _subtitleBackground = null!;
        _subtitlePosition = null!;
        _leftDeadZone = null!;
        _rightDeadZone = null!;
        _triggerDeadZone = null!;
        _horizontalSensitivity = null!;
        _verticalSensitivity = null!;
        _leftCurve = null!;
        _rightCurve = null!;
        _invertHorizontal = null!;
        _invertVertical = null!;
        _controllerVibration = null!;

        tabs.Items.Add(BuildGeneralTab(current, gameFolder, settingsPath, logDirectory));
        tabs.Items.Add(BuildControllerTab(current));
        tabs.Items.Add(BuildVideoTab(current));
        tabs.Items.Add(BuildAudioTab(current));
        tabs.Items.Add(BuildSubtitlesTab(current));
        tabs.Items.Add(BuildAboutTab());

        root.Children.Add(tabs);
        Content = root;
    }

    private TabItem BuildGeneralTab(LauncherSettings current, string gameFolder, string settingsPath, string logDirectory)
    {
        var panel = NewPanel();
        AddSectionTitle(panel, "PERFIL DO JOGADOR");
        _playerName = AddTextField(panel, "Nome do jogador", current.PlayerName, 24,
            "Nome de exibição salvo no perfil local do launcher.");
        _playerTag = AddTextField(panel, "Tag / clã (opcional)", current.PlayerTag, 12,
            "Identificador curto para personalização futura do perfil.");

        AddNotice(panel, "Esses dados ficam salvos localmente. Ainda não são enviados nem sincronizados com o jogo.", true);
        AddSectionTitle(panel, "PREFERÊNCIAS DO LAUNCHER");
        _startupUpdates = AddOption(panel, "Verificar atualizações ao abrir",
            "Consulta a Release oficial sem bloquear a inicialização se a rede falhar.", current.CheckUpdatesOnStartup);
        _confirmLaunch = AddOption(panel, "Confirmar antes de iniciar o jogo",
            "Exibe uma confirmação antes de executar o modo offline.", current.ConfirmBeforeLaunch);
        _persistLogs = AddOption(panel, "Salvar logs no computador",
            "Mantém um histórico local para facilitar diagnósticos.", current.PersistLogs);

        AddSectionTitle(panel, "INSTALAÇÃO DO JOGO");
        AddText(panel, string.IsNullOrWhiteSpace(gameFolder) ? "Nenhuma pasta selecionada." : gameFolder, 11);
        AddText(panel, "Configurações: " + settingsPath + Environment.NewLine + "Logs: " + logDirectory, 10, muted: true);
        return MakeTab("GERAL", panel);
    }

    private TabItem BuildControllerTab(LauncherSettings current)
    {
        var panel = NewPanel();
        AddSectionTitle(panel, "DISPOSITIVO");
        AddText(panel, "A configuração abaixo já pode ser salva no perfil local. A detecção ao vivo, o teste físico e a aplicação durante a partida dependem da integração de entrada com o jogo; não serão simulados nesta etapa.", 11);
        AddNotice(panel, "CONTROLE: PREFERÊNCIAS SALVAS • INTEGRAÇÃO IN-GAME PENDENTE", true);
        AddSectionTitle(panel, "ZONAS MORTAS");
        _leftDeadZone = AddSlider(panel, "Dead zone do analógico esquerdo", current.ControllerLeftDeadZone, 0, 50, "%");
        _rightDeadZone = AddSlider(panel, "Dead zone do analógico direito", current.ControllerRightDeadZone, 0, 50, "%");
        _triggerDeadZone = AddSlider(panel, "Dead zone dos gatilhos", current.ControllerTriggerDeadZone, 0, 50, "%");
        AddSectionTitle(panel, "SENSIBILIDADE E RESPOSTA");
        _horizontalSensitivity = AddSlider(panel, "Sensibilidade horizontal", current.ControllerHorizontalSensitivity, 10, 200, "%");
        _verticalSensitivity = AddSlider(panel, "Sensibilidade vertical", current.ControllerVerticalSensitivity, 10, 200, "%");
        _leftCurve = AddCombo(panel, "Curva de resposta — analógico esquerdo", current.ControllerLeftCurve,
            "Linear", "Suave", "Agressiva");
        _rightCurve = AddCombo(panel, "Curva de resposta — analógico direito", current.ControllerRightCurve,
            "Linear", "Suave", "Agressiva");
        AddSectionTitle(panel, "INVERSÃO E VIBRAÇÃO");
        _invertHorizontal = AddOption(panel, "Inverter eixo horizontal", "", current.ControllerInvertHorizontal);
        _invertVertical = AddOption(panel, "Inverter eixo vertical", "", current.ControllerInvertVertical);
        _controllerVibration = AddOption(panel, "Permitir vibração", "Será aplicada somente se o jogo/dispositivo oferecer suporte.", current.ControllerVibration);
        return MakeTab("CONTROLE", panel);
    }

    private TabItem BuildVideoTab(LauncherSettings current)
    {
        var panel = NewPanel();
        AddSectionTitle(panel, "VÍDEO E GRÁFICOS");
        AddText(panel, "Preferências preparadas para o próximo patch. A lista será conectada apenas às opções que forem confirmadas nos arquivos/configuração do jogo.", 11);
        AddNotice(panel, "ALTERAÇÕES GRÁFICAS AINDA NÃO SÃO APLICADAS AO EXECUTÁVEL", true);
        _resolution = AddCombo(panel, "Resolução", current.VideoResolution, "Padrão do jogo", "1280 × 720", "1600 × 900", "1920 × 1080", "2560 × 1440", "3840 × 2160");
        _graphicsPreset = AddCombo(panel, "Qualidade gráfica", current.GraphicsPreset, "Padrão do jogo", "Baixa", "Média", "Alta", "Personalizada");
        _fpsLimit = AddCombo(panel, "Limite de FPS", current.FpsLimit.ToString(), "60", "75", "90", "120", "144", "165", "180", "200", "240");
        _fullscreen = AddOption(panel, "Tela cheia", "Preferência salva; aplicação depende do suporte do jogo.", current.VideoFullscreen);
        _vSync = AddOption(panel, "Sincronização vertical (V-Sync)", "Preferência salva; aplicação depende do suporte do jogo.", current.VideoVSync);
        _showFps = AddOption(panel, "Exibir contador de FPS", "Não cria overlay nesta etapa.", current.VideoShowFps);
        AddSectionTitle(panel, "OBSERVAÇÃO SOBRE FPS");
        AddText(panel, "O limite de 60 a 240 FPS aparece como opção de configuração, mas não altera o jogo até identificarmos um mecanismo compatível com a versão instalada. Não será instalado mod, patch binário ou injetor de FPS sem validação.", 11);
        return MakeTab("VÍDEO E GRÁFICOS", panel);
    }

    private TabItem BuildAudioTab(LauncherSettings current)
    {
        var panel = NewPanel();
        AddSectionTitle(panel, "ÁUDIO");
        AddText(panel, "Níveis e preferências ficam salvos localmente. A aplicação no jogo depende de encontrarmos os controles de áudio oficiais do patch.", 11);
        AddNotice(panel, "CONFIGURAÇÕES DE ÁUDIO AINDA NÃO CONECTADAS AO JOGO", true);
        _muteAudio = AddOption(panel, "Silenciar áudio", "Preferência geral.", current.AudioMuted);
        _masterVolume = AddSlider(panel, "Volume geral", current.AudioMasterVolume, 0, 100, "%");
        _musicVolume = AddSlider(panel, "Volume da música", current.AudioMusicVolume, 0, 100, "%");
        _effectsVolume = AddSlider(panel, "Volume dos efeitos", current.AudioEffectsVolume, 0, 100, "%");
        return MakeTab("ÁUDIO", panel);
    }

    private TabItem BuildSubtitlesTab(LauncherSettings current)
    {
        var panel = NewPanel();
        AddSectionTitle(panel, "LEGENDAS");
        AddText(panel, "A interface e as preferências de exibição estão preparadas. As falas em pt-BR, nomes e sincronização dependem dos arquivos de diálogo e temporização do patch; nenhum texto será inventado.", 11);
        AddNotice(panel, "TRADUÇÃO E SINCRONIZAÇÃO AGUARDAM OS ARQUIVOS DO PATCH", true);
        _subtitlesEnabled = AddOption(panel, "Ativar legendas", "Exibe falas de entrada dos personagens quando houver dados verificados.", current.SubtitlesEnabled);
        _subtitleSize = AddCombo(panel, "Tamanho da legenda", current.SubtitleSize, "Pequeno", "Médio", "Grande", "Muito grande");
        _subtitleColor = AddCombo(panel, "Cor do texto", current.SubtitleColor, "Branco", "Amarelo", "Ciano", "Verde");
        _subtitleBackground = AddCombo(panel, "Fundo da legenda", current.SubtitleBackground, "Transparente", "Escuro", "Preto");
        _subtitlePosition = AddCombo(panel, "Posição", current.SubtitlePosition, "Inferior", "Superior");
        return MakeTab("LEGENDAS", panel);
    }

    private TabItem BuildAboutTab()
    {
        var about = NewPanel();
        AddSectionTitle(about, "SOBRE O LAUNCHER");
        AddText(about, "REVIVAL REVOLUTION LAUNCHER", 21, bold: true);
        AddText(about, "Uma interface moderna para organizar e iniciar a experiência Metal Revolution, mantendo a compatibilidade com os scripts e componentes originais da distribuição.", 12);
        AddSectionTitle(about, "CRÉDITOS");
        AddCredit(about, "CRIADOR ORIGINAL DO PROJETO COMUNITÁRIO", "@NC_KuroRaiden (Twitter / X)", "https://x.com/NC_KuroRaiden");
        AddCredit(about, "LAUNCHER • DESENVOLVIMENTO E MANUTENÇÃO", "@lngadesupport (GitHub)", "https://github.com/lngadesupport");
        AddSectionTitle(about, "VERSÃO");
        var version = typeof(LauncherSettingsWindow).Assembly.GetName().Version;
        AddText(about, version is null ? "Versão não identificada" : $"v{version.Major}.{version.Minor}.{version.Build}", 14, bold: true);
        AddText(about, "Projeto comunitário independente. Os créditos do criador original são mantidos; este launcher é uma interface complementar e não reivindica a autoria do jogo Metal Revolution, desenvolvido por NEXT Studios.", 10, muted: true);
        return MakeTab("SOBRE", about);
    }

    private LauncherSettings ReadValues(string playerName, string playerTag) => new()
    {
        PlayerName = playerName,
        PlayerTag = playerTag,
        CheckUpdatesOnStartup = _startupUpdates.IsChecked == true,
        ConfirmBeforeLaunch = _confirmLaunch.IsChecked == true,
        PersistLogs = _persistLogs.IsChecked == true,
        ControllerLeftDeadZone = (int)_leftDeadZone.Value,
        ControllerRightDeadZone = (int)_rightDeadZone.Value,
        ControllerTriggerDeadZone = (int)_triggerDeadZone.Value,
        ControllerHorizontalSensitivity = (int)_horizontalSensitivity.Value,
        ControllerVerticalSensitivity = (int)_verticalSensitivity.Value,
        ControllerLeftCurve = Selected(_leftCurve, "Linear"),
        ControllerRightCurve = Selected(_rightCurve, "Linear"),
        ControllerInvertHorizontal = _invertHorizontal.IsChecked == true,
        ControllerInvertVertical = _invertVertical.IsChecked == true,
        ControllerVibration = _controllerVibration.IsChecked == true,
        VideoResolution = Selected(_resolution, "Padrão do jogo"),
        GraphicsPreset = Selected(_graphicsPreset, "Padrão do jogo"),
        FpsLimit = int.TryParse(Selected(_fpsLimit, "60"), out var fps) ? fps : 60,
        VideoFullscreen = _fullscreen.IsChecked == true,
        VideoVSync = _vSync.IsChecked == true,
        VideoShowFps = _showFps.IsChecked == true,
        AudioMuted = _muteAudio.IsChecked == true,
        AudioMasterVolume = (int)_masterVolume.Value,
        AudioMusicVolume = (int)_musicVolume.Value,
        AudioEffectsVolume = (int)_effectsVolume.Value,
        SubtitlesEnabled = _subtitlesEnabled.IsChecked == true,
        SubtitleSize = Selected(_subtitleSize, "Médio"),
        SubtitleColor = Selected(_subtitleColor, "Branco"),
        SubtitleBackground = Selected(_subtitleBackground, "Escuro"),
        SubtitlePosition = Selected(_subtitlePosition, "Inferior")
    };

    private void ApplyValues(LauncherSettings value)
    {
        _playerName.Text = value.PlayerName;
        _playerTag.Text = value.PlayerTag;
        _startupUpdates.IsChecked = value.CheckUpdatesOnStartup;
        _confirmLaunch.IsChecked = value.ConfirmBeforeLaunch;
        _persistLogs.IsChecked = value.PersistLogs;
        _leftDeadZone.Value = value.ControllerLeftDeadZone;
        _rightDeadZone.Value = value.ControllerRightDeadZone;
        _triggerDeadZone.Value = value.ControllerTriggerDeadZone;
        _horizontalSensitivity.Value = value.ControllerHorizontalSensitivity;
        _verticalSensitivity.Value = value.ControllerVerticalSensitivity;
        Select(_leftCurve, value.ControllerLeftCurve);
        Select(_rightCurve, value.ControllerRightCurve);
        _invertHorizontal.IsChecked = value.ControllerInvertHorizontal;
        _invertVertical.IsChecked = value.ControllerInvertVertical;
        _controllerVibration.IsChecked = value.ControllerVibration;
        Select(_resolution, value.VideoResolution);
        Select(_graphicsPreset, value.GraphicsPreset);
        Select(_fpsLimit, value.FpsLimit.ToString());
        _fullscreen.IsChecked = value.VideoFullscreen;
        _vSync.IsChecked = value.VideoVSync;
        _showFps.IsChecked = value.VideoShowFps;
        _muteAudio.IsChecked = value.AudioMuted;
        _masterVolume.Value = value.AudioMasterVolume;
        _musicVolume.Value = value.AudioMusicVolume;
        _effectsVolume.Value = value.AudioEffectsVolume;
        _subtitlesEnabled.IsChecked = value.SubtitlesEnabled;
        Select(_subtitleSize, value.SubtitleSize);
        Select(_subtitleColor, value.SubtitleColor);
        Select(_subtitleBackground, value.SubtitleBackground);
        Select(_subtitlePosition, value.SubtitlePosition);
    }

    private static LauncherSettings Copy(LauncherSettings s) => new()
    {
        PlayerName = s.PlayerName, PlayerTag = s.PlayerTag,
        CheckUpdatesOnStartup = s.CheckUpdatesOnStartup, ConfirmBeforeLaunch = s.ConfirmBeforeLaunch,
        PersistLogs = s.PersistLogs,
        ControllerLeftDeadZone = s.ControllerLeftDeadZone, ControllerRightDeadZone = s.ControllerRightDeadZone,
        ControllerTriggerDeadZone = s.ControllerTriggerDeadZone,
        ControllerHorizontalSensitivity = s.ControllerHorizontalSensitivity,
        ControllerVerticalSensitivity = s.ControllerVerticalSensitivity,
        ControllerLeftCurve = s.ControllerLeftCurve, ControllerRightCurve = s.ControllerRightCurve,
        ControllerInvertHorizontal = s.ControllerInvertHorizontal, ControllerInvertVertical = s.ControllerInvertVertical,
        ControllerVibration = s.ControllerVibration,
        VideoResolution = s.VideoResolution, GraphicsPreset = s.GraphicsPreset, FpsLimit = s.FpsLimit,
        VideoFullscreen = s.VideoFullscreen, VideoVSync = s.VideoVSync, VideoShowFps = s.VideoShowFps,
        AudioMuted = s.AudioMuted, AudioMasterVolume = s.AudioMasterVolume,
        AudioMusicVolume = s.AudioMusicVolume, AudioEffectsVolume = s.AudioEffectsVolume,
        SubtitlesEnabled = s.SubtitlesEnabled, SubtitleSize = s.SubtitleSize,
        SubtitleColor = s.SubtitleColor, SubtitleBackground = s.SubtitleBackground,
        SubtitlePosition = s.SubtitlePosition
    };

    private static string Selected(ComboBox combo, string fallback) => combo.SelectedItem as string ?? fallback;
    private static void Select(ComboBox combo, string value)
    {
        var options = combo.ItemsSource as IEnumerable<string>;
        combo.SelectedItem = options is not null && options.Contains(value)
            ? value
            : options?.FirstOrDefault();
    }

    private static StackPanel NewPanel() => new() { Margin = new Thickness(18) };
    private static TabItem MakeTab(string header, UIElement content) => new()
    {
        Header = header,
        Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(18, 21, 34))
        }
    };

    private static void AddCredit(Panel parent, string role, string account, string url)
    {
        AddText(parent, role, 10, bold: true, muted: true);
        var link = new Button
        {
            Content = account, HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0, 3, 8, 3), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 118)),
            FontSize = 13, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand
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
        parent.Children.Add(link);
    }

    private static void AddSectionTitle(Panel parent, string title)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title, Margin = new Thickness(0, 12, 0, 6), FontWeight = FontWeights.Bold,
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 118))
        });
    }

    private static void AddText(Panel parent, string value, double size = 11, bool bold = false, bool muted = false)
    {
        parent.Children.Add(new TextBlock
        {
            Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Margin = new Thickness(0, 3, 0, 7),
            Foreground = muted ? new SolidColorBrush(Color.FromRgb(155, 160, 178)) :
                new SolidColorBrush(Color.FromRgb(205, 207, 219))
        });
    }

    private static void AddNotice(Panel parent, string message, bool warning)
    {
        parent.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(42, 34, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(119, 83, 58)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10), Margin = new Thickness(0, 5, 0, 10),
            Child = new TextBlock
            {
                Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 10,
                FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 120))
            }
        });
    }

    private static TextBox AddTextField(Panel parent, string title, string value, int maxLength, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5)
        });
        var input = new TextBox
        {
            Text = value ?? "", MaxLength = maxLength, Padding = new Thickness(9, 7, 9, 7),
            Background = new SolidColorBrush(Color.FromRgb(25, 28, 43)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 80, 100)), CaretBrush = Brushes.White, FontSize = 13
        };
        panel.Children.Add(input);
        AddText(panel, description, 10, muted: true);
        parent.Children.Add(panel);
        return input;
    }

    private static CheckBox AddOption(Panel parent, string title, string description, bool isChecked)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var check = new CheckBox
        {
            Content = title, IsChecked = isChecked, FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, VerticalContentAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(check);
        if (!string.IsNullOrWhiteSpace(description))
            AddText(panel, description, 10, muted: true);
        parent.Children.Add(panel);
        return check;
    }

    private static Slider AddSlider(Panel parent, string title, int value, int minimum, int maximum, string suffix)
    {
        var heading = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        heading.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold });
        var readout = new TextBlock
        {
            Text = value + suffix, Foreground = new SolidColorBrush(Color.FromRgb(255, 150, 160)),
            FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(readout, Dock.Right);
        heading.Children.Add(readout);
        parent.Children.Add(heading);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(value, minimum, maximum), TickFrequency = 5, IsSnapToTickEnabled = true };
        slider.ValueChanged += (_, _) => readout.Text = ((int)slider.Value) + suffix;
        parent.Children.Add(slider);
        return slider;
    }

    private static ComboBox AddCombo(Panel parent, string title, string selected, params string[] options)
    {
        var group = new StackPanel { Margin = new Thickness(0, 7, 0, 5) };
        group.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        var combo = new ComboBox
        {
            ItemsSource = options, SelectedItem = options.Contains(selected) ? selected : options[0],
            Padding = new Thickness(7), Background = new SolidColorBrush(Color.FromRgb(25, 28, 43)),
            Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(75, 80, 100))
        };
        group.Children.Add(combo);
        parent.Children.Add(group);
        return combo;
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text, Padding = new Thickness(14, 10, 14, 10),
        Background = new SolidColorBrush(primary ? Color.FromRgb(180, 35, 49) : Color.FromRgb(40, 44, 60)),
        Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(100, 105, 125)),
        BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand
    };
}
