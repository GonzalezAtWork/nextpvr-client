using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NextPvr.Core;
using System.Xml.Linq;

namespace NextPvr.Desktop;

public sealed class MainWindow : Window
{
    private readonly ContentControl body = new();
    private readonly TextBlock status = new() { Text = "Conecte seu servidor para começar", FontSize = 16, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromMinutes(2) };
    private readonly List<Button> menu = [];
    private NextPvrClient? client;
    private PlayerWindow? player;
    private ListBox? activeList;
    private bool busy;
    private static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextPvrClient", "server.xml");

    public MainWindow()
    {
        Title = "NextPVR • Sala"; Width = 1320; Height = 820; MinWidth = 900; MinHeight = 600;
        Background = new SolidColorBrush(Color.Parse("#101822"));
        var grid = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new Thickness(32) };
        var sidebar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 0, 0, 28) };
        sidebar.Children.Add(new TextBlock { Text = "NextPVR", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Brushes.Cyan, Margin = new Thickness(0, 0, 0, 8) });
        AddMenu(sidebar, "TV ao vivo", ShowChannels);
        AddMenu(sidebar, "Gravações", ShowRecordings);
        AddMenu(sidebar, "Configurações", () => { ShowSettings(); return Task.CompletedTask; });
        grid.Children.Add(sidebar); Grid.SetRow(body, 1); grid.Children.Add(body);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 20, 0, 0) };
        footer.Children.Add(status);
        footer.Children.Add(new TextBlock { Text = "↑ ↓ ← → navegar    OK / Enter selecionar    Voltar / Esc retornar    F11 tela cheia", Foreground = Brushes.Gray, FontSize = 14 });
        Grid.SetRow(footer, 2); grid.Children.Add(footer); Content = grid;
        AddHandler(KeyDownEvent, RemoteKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        heartbeat.Tick += async (_, _) => { if (client != null && !busy) await Run(async () => await client.KeepAliveAsync(lifetime.Token), false); };
        Closed += (_, _) => { lifetime.Cancel(); heartbeat.Stop(); player?.Close(); client?.Dispose(); lifetime.Dispose(); };
        ShowSettings();
    }
    private void AddMenu(StackPanel panel, string title, Func<Task> action)
    {
        var button = MakeButton(title, async () => await Run(action));
        button.HorizontalAlignment = HorizontalAlignment.Stretch; menu.Add(button); panel.Children.Add(button);
    }
    private static Button MakeButton(string text, Func<Task> action)
    {
        var b = new Button { Content = text };
        b.Click += async (_, _) => await action(); return b;
    }
    private async Task Run(Func<Task> action, bool showBusy = true)
    {
        if (busy) return;
        busy = true;
        if (showBusy) status.Text = "Carregando…";
        try { await action(); }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) status.Text = "Tempo de conexão excedido. Tente novamente."; }
        catch (Exception ex) { status.Text = ex is NextPvrException or ArgumentException ? ex.Message : "Não foi possível completar a operação. Verifique o servidor e sua conexão."; }
        finally { busy = false; }
    }
    private NextPvrClient RequireClient() => client ?? throw new ArgumentException("Conecte o servidor em Configurações.");
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 30, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 18) };

    private void ShowSettings()
    {
        activeList = null;
        var panel = new StackPanel { Spacing = 18, MaxWidth = 650, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Conectar ao NextPVR"));
        panel.Children.Add(new TextBlock { Text = "Informe o endereço e o PIN de acesso remoto do seu servidor.", FontSize = 18, TextWrapping = TextWrapping.Wrap });
        string saved = "http://192.168.1.10:8866";
        try { if (File.Exists(SettingsFile)) saved = XElement.Load(SettingsFile).Value; } catch { /* A corrupt preference must not prevent startup. */ }
        var server = new TextBox { Text = saved, Watermark = "http://servidor:8866", FontSize = 22, MinWidth = 540 };
        var pin = new TextBox { Watermark = "PIN", PasswordChar = '●', FontSize = 22 };
        panel.Children.Add(new TextBlock { Text = "Servidor", FontSize = 16 }); panel.Children.Add(server);
        panel.Children.Add(new TextBlock { Text = "PIN de acesso", FontSize = 16 }); panel.Children.Add(pin);
        panel.Children.Add(MakeButton("Conectar", async () => await Run(async () =>
        {
            var candidate = new NextPvrClient(server.Text ?? "", pin.Text ?? "");
            try { await candidate.ConnectAsync(lifetime.Token); }
            catch { candidate.Dispose(); throw; }
            player?.Close(); client?.Dispose(); client = candidate;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            new XElement("server", candidate.Server.AbsoluteUri).Save(SettingsFile);
            heartbeat.Start(); await ShowChannels();
        })));
        panel.Children.Add(new TextBlock { Text = "O endereço é salvo neste dispositivo. O PIN é solicitado a cada abertura.", FontSize = 15, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        body.Content = panel; Dispatcher.UIThread.Post(() => server.Focus());
    }
    private async Task ShowChannels()
    {
        var api = RequireClient();
        var channels = await api.ChannelsAsync(lifetime.Token);
        var grid = new Grid { RowDefinitions = new("Auto,Auto,*"), ColumnDefinitions = new("3*,2*") };
        grid.Children.Add(Heading("TV ao vivo"));
        var search = new TextBox { Watermark = "Buscar canal por nome ou número", FontSize = 22, Margin = new Thickness(0, 0, 0, 22) };
        Grid.SetRow(search, 1); Grid.SetColumnSpan(search, 2); grid.Children.Add(search);
        var list = new ListBox { Focusable = true, ItemsSource = channels, FontSize = 22, Background = new SolidColorBrush(Color.Parse(Presentation.Background)), Margin = new Thickness(0, 0, 18, 0) };
        activeList = list;
        var details = new StackPanel { Spacing = 14 };
        details.Children.Add(new TextBlock { Text = "Selecione um canal para assistir ou consultar o guia.", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        var card = new Border { Child = details, Padding = new Thickness(24), Background = new SolidColorBrush(Color.Parse(Presentation.Surface)), CornerRadius = new CornerRadius(12) };
        Grid.SetRow(list, 2); grid.Children.Add(list); Grid.SetRow(card, 2); Grid.SetColumn(card, 1); grid.Children.Add(card); body.Content = grid;
        search.TextChanged += (_, _) =>
        {
            var filtered = channels.Where(c => Presentation.Matches(c, search.Text)).ToArray();
            list.ItemsSource = filtered; details.Children.Clear();
            if (filtered.Length > 0) list.SelectedIndex = 0;
            else details.Children.Add(Heading("Nenhum canal encontrado"));
            status.Text = $"{filtered.Length} de {channels.Count} canais";
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Channel c) return;
            details.Children.Clear(); details.Children.Add(Heading(c.Name));
            details.Children.Add(MakeButton("▶ Assistir", async () => await Run(async () => await Play(await api.LiveUriAsync(c.Id, lifetime.Token), c.Name, true, c.Id))));
            details.Children.Add(MakeButton("Guia • próximas 24 horas", async () => await Run(async () => await ShowGuide(c))));
        };
        list.DoubleTapped += async (_, _) => { if (list.SelectedItem is Channel c) await Run(async () => await Play(await api.LiveUriAsync(c.Id, lifetime.Token), c.Name, true, c.Id)); };
        list.KeyDown += async (_, e) => { if (e.Key == Key.Enter && list.SelectedItem is Channel c) { e.Handled = true; await Run(async () => await Play(await api.LiveUriAsync(c.Id, lifetime.Token), c.Name, true, c.Id)); } };
        list.KeyDown += (_,e) => { if (e.Key == Key.Up && list.SelectedIndex <= 0) { search.Focus(); e.Handled = true; } };
        status.Text = channels.Count == 0 ? "Nenhum canal configurado no servidor." : $"{channels.Count} canais • conectado a {api.Server.Host}";
        if (channels.Count > 0) list.SelectedIndex = 0;
        Dispatcher.UIThread.Post(() => list.Focus());
    }
    private async Task ShowGuide(Channel channel)
    {
        var api = RequireClient();
        var programmes = await api.GuideAsync(channel.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), lifetime.Token);
        var panel = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), ColumnDefinitions = new("*") };
        panel.Children.Add(Heading("Guia • " + channel.Name));
        var list = new ListBox { Focusable = true, ItemsSource = programmes, FontSize = 22 }; activeList = list;
        Grid.SetRow(list, 1); panel.Children.Add(list);
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 18, MaxHeight = 130, Margin = new Thickness(0, 12) };
        Grid.SetRow(description, 2); panel.Children.Add(description);
        list.SelectionChanged += (_, _) => description.Text = (list.SelectedItem as Programme)?.Description;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(MakeButton("▶ Assistir canal", async () => await Run(async () => await Play(await api.LiveUriAsync(channel.Id, lifetime.Token), channel.Name, true, channel.Id))));
        actions.Children.Add(MakeButton("Gravar programa", async () => await Run(async () =>
        {
            if (list.SelectedItem is not Programme p) return;
            await api.ScheduleAsync(p.Id, lifetime.Token); status.Text = "Gravação agendada: " + p.Name;
        })));
        actions.Children.Add(MakeButton("Voltar", async () => await Run(ShowChannels)));
        Grid.SetRow(actions, 3); panel.Children.Add(actions); body.Content = panel;
        status.Text = programmes.Count == 0 ? "Sem programação. Atualize o EPG no servidor NextPVR." : "Escolha um programa para agendar a gravação.";
        if (programmes.Count > 0) list.SelectedIndex = 0;
        Dispatcher.UIThread.Post(() => list.Focus());
    }
    private async Task ShowRecordings()
    {
        var api = RequireClient(); var recordings = await api.RecordingsAsync(lifetime.Token);
        var panel = new StackPanel { Spacing = 16 }; panel.Children.Add(Heading("Gravações"));
        var list = new ListBox { Focusable = true, ItemsSource = recordings, FontSize = 21, Height = 420 }; activeList = list; panel.Children.Add(list);
        var description = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap, MaxHeight = 80 }; panel.Children.Add(description);
        list.SelectionChanged += (_, _) => description.Text = (list.SelectedItem as Recording)?.Description;
        async Task Watch() { if (list.SelectedItem is Recording r) { if (!r.Playable) throw new ArgumentException("Esta gravação ainda não está disponível para reprodução."); await Play(await api.RecordingUriAsync(r.Id, lifetime.Token), r.Name, false); } }
        panel.Children.Add(MakeButton("▶ Reproduzir", async () => await Run(Watch)));
        list.DoubleTapped += async (_, _) => await Run(Watch);
        list.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Run(Watch); } };
        body.Content = panel; status.Text = recordings.Count == 0 ? "Você ainda não tem gravações." : $"{recordings.Count} gravações e agendamentos";
        if (recordings.Count > 0) list.SelectedIndex = 0;
        Dispatcher.UIThread.Post(() => list.Focus());
    }
    private Task Play(Uri uri, string title, bool live, int? channelId = null)
    {
        player?.Close(); player = new PlayerWindow(uri, title, live, live ? new LiveChannelNavigator(RequireClient(),channelId,title) : null); player.Show(this); status.Text = "Reproduzindo: " + title; return Task.CompletedTask;
    }
    private void RemoteKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.F11) { WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; e.Handled = true; return; }
        if (e.Key is Key.Escape or Key.BrowserBack) { _ = Run(ShowChannels); e.Handled = true; return; }
        if (e.Source is TextBox)
        {
            if (e.Key is Key.Up or Key.Down or Key.Enter) { RemoteFocus.Move(this, e.Key == Key.Up); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Left && activeList != null) { menu[0].Focus(); e.Handled = true; }
        else if (e.Key == Key.Right && menu.Any(m => m.IsFocused) && activeList != null) { activeList.Focus(); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down && e.Source is Button)
        { RemoteFocus.Move(this, e.Key == Key.Up); e.Handled = true; }
        else if (e.Key == Key.Right && activeList?.IsKeyboardFocusWithin == true)
        { RemoteFocus.Move(this, false); e.Handled = true; }
    }
}


