using NextPvr.Core;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.NUI.Components;
using System.Xml.Linq;

namespace NextPvr.Tizen;

internal static class Program
{
    private static void Main(string[] args) { using var app = new TvApp(); app.Run(args); }
}

public sealed class TvApp : NUIApplication
{
    private View? page;
    private TextLabel status = null!;
    private NextPvrClient? client;
    private VideoView? video;
    private readonly List<(View view, Action? action)> focus = [];
    private int selected;
    private bool busy;
    private bool playing;
    private View? controls;
    private global::Tizen.NUI.Timer? hideControls;
    private View? rowsContainer;
    private View? rowsViewport;
    private readonly Dictionary<View, int> rowPositions = new();
    private readonly Dictionary<View, Action> rowSelections = new();
    private int rowOffset;
    private bool suppressClick;
    private int firstRow;
    private bool channelList;
    private LiveChannelNavigator? liveChannels;
    private TextLabel? feedName;
    private readonly CancellationTokenSource lifetime = new();
    private global::Tizen.NUI.Timer? heartbeat;
    private static global::Tizen.NUI.Window AppWindow =>
#if TIZEN65
        global::Tizen.NUI.Window.Instance;
#else
        global::Tizen.NUI.Window.Default;
#endif
    private static string SettingsFile => System.IO.Path.Combine(global::Tizen.Applications.Application.Current.DirectoryInfo.Data, "server.xml");
    protected override void OnCreate()
    {
        base.OnCreate(); AppWindow.BackgroundColor = new Color(0.06f, 0.09f, 0.13f, 1);
        AppWindow.KeyEvent += (_, e) =>
        {
            if (e.Key.State != Key.StateType.Down) return;
            var key = e.Key.KeyPressedName;
            if (key is "XF86Back" or "Escape" or "BackSpace")
            {
                if (playing) { StopVideo(); _ = Run(ShowChannels); }
                else if (client != null) _ = Run(ShowChannels);
                return;
            }
            if (playing)
            {
                bool hidden = controls?.Visibility == false;
                if (!hidden) suppressClick = false;
                RevealControls();
                if (liveChannels != null && key is "XF86RaiseChannel" or "XF86ChannelUp" or "ChannelUp" or "Prior") { _ = SwitchChannel(1); return; }
                if (liveChannels != null && key is "XF86LowerChannel" or "XF86ChannelDown" or "ChannelDown" or "Next") { _ = SwitchChannel(-1); return; }
                if (hidden && key is "Return" or "KP_Enter" or "Left" or "Right" or "Up" or "Down") { suppressClick = key is "Return" or "KP_Enter"; Select(0); return; }
            }
            if (focus.Count == 0) return;
            if (!playing && rowPositions.ContainsKey(focus[selected].view))
            {
                if (key == "Up") { Select(selected == firstRow ? (channelList ? 3 : 0) : selected - 1); return; }
                if (key == "Down") { Select(Math.Min(selected + 1,focus.Count - 1)); return; }
                if (key == "Right" && channelList) { Select(4); return; }
                if (key == "Left") { Select(0); return; }
            }
            if (channelList && key == "Down" && selected == 3 && focus.Count > firstRow) { Select(firstRow); return; }
            if (channelList && key == "Left" && selected is 4 or 5 && focus.Count > firstRow) { Select(firstRow); return; }
            if (key is "Up") Select(selected - 1);
            else if (key is "Down") Select(selected + 1);
            else if (focus[selected].view is TextField) return; // Native input/IME owns editing keys.
            else if (key is "Left") Select(selected - 1);
            else if (key is "Right") Select(selected + 1);
            // Buttons generate Clicked for OK/Enter through the native control.
        };
        AppWindow.TouchEvent += (_, _) => { suppressClick = false; if (playing) RevealControls(); };
        AppWindow.WheelEvent += (_, _) => { if (playing) RevealControls(); };
        ShowSettings();
    }
    private void Page(string title, bool navigation = true)
    {
        StopVideo();
        if (page != null) { AppWindow.Remove(page); page.Dispose(); }
        page = new View { Size = new Size(1920, 1080) }; AppWindow.Add(page); focus.Clear(); selected = 0;
        rowPositions.Clear(); rowSelections.Clear(); rowsContainer = null; rowsViewport = null; rowOffset = 0; firstRow = 3; channelList = false;
        page.HoverEvent += (_, _) => { if (playing) RevealControls(); return false; };
        page.TouchEvent += (_, _) => { if (playing) RevealControls(); return false; };
        if (navigation) { Label("NextPVR",70,45,320,70,38).TextColor = new Color(0.086f,0.72f,0.83f,1); Label(title,70,150,1750,65,36); }
        if (navigation)
        {
            Button("TV ao vivo", 430, 45, 380, ShowChannels);
            Button("Gravações", 830, 45, 380, ShowRecordings);
            Button("Configurações", 1230, 45, 500, () => { ShowSettings(); return Task.CompletedTask; });
        }
        status = Label("", 70, 975, 1750, 70, 22);
    }
    private TextLabel Label(string text, int x, int y, int width, int height, float size)
    {
        var label = new TextLabel { Text = text, Position = new Position(x, y), Size = new Size(width, height), PointSize = size, TextColor = Color.White, MultiLine = true };
        page!.Add(label); return label;
    }
    private Button Button(string text, int x, int y, int width, Func<Task> action, View? parent = null)
    {
        var button = new Button { Text = text, PointSize = 26, TextColor = Color.White, Position = new Position(x, y), Size = new Size(width, 72), Focusable = true };
        int index = focus.Count;
        Action invoke = () => { if (playing && suppressClick) { suppressClick = false; return; } _ = Run(action); };
        button.Clicked += (_, _) => { Select(index); invoke(); };
        focus.Add((button, invoke)); (parent ?? page!).Add(button); return button;
    }
    private void Select(int index)
    {
        if (focus.Count == 0) return;
        selected = Math.Max(0, Math.Min(index, focus.Count - 1));
        for (int i = 0; i < focus.Count; i++)
            focus[i].view.BackgroundColor = i == selected ? new Color(0.086f, 0.72f, 0.83f, playing ? 0.75f : 1) : new Color(0.14f, 0.2f, 0.27f, playing ? 0.6f : 1);
        var current = focus[selected].view;
        if (rowPositions.TryGetValue(current, out int row))
        {
            int top = row * 90;
            ScrollRows(top < rowOffset ? top : top + 72 > rowOffset + 600 ? top + 72 - 600 : rowOffset);
            if (rowSelections.TryGetValue(current, out var selectRow)) selectRow();
        }
        FocusManager.Instance.SetCurrentFocusView(current);
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true;
        try { await action(); }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) status.Text = ex is NextPvrException or ArgumentException ? ex.Message : "Falha na conexão ou reprodução. Verifique seu servidor."; }
        finally { busy = false; }
    }
    private NextPvrClient Api => client ?? throw new ArgumentException("Conecte o servidor em Configurações.");
    private void ShowSettings()
    {
        Page("NextPVR • Conectar");
        string address = "http://192.168.1.10:8866";
        try { if (File.Exists(SettingsFile)) address = XElement.Load(SettingsFile).Value; } catch { }
        Label("Servidor", 70, 250, 1500, 50, 26);
        var server = new TextField { Text = address, Position = new Position(70, 320), Size = new Size(1400, 80), PointSize = 30, Focusable = true };
        page!.Add(server); focus.Add((server, null));
        Label("PIN de acesso remoto", 70, 430, 1500, 50, 26);
        var pin = new TextField { Position = new Position(70, 500), Size = new Size(600, 80), PointSize = 30, Focusable = true };
        pin.HiddenInputSettings = new PropertyMap().Add("mode", new PropertyValue(1));
        page.Add(pin); focus.Add((pin, null));
        Button("Conectar", 70, 650, 600, async () =>
        {
            var candidate = new NextPvrClient(server.Text, pin.Text);
            try { await candidate.ConnectAsync(lifetime.Token); } catch { candidate.Dispose(); throw; }
            client?.Dispose(); client = candidate;
            new XElement("server", candidate.Server.AbsoluteUri).Save(SettingsFile);
            heartbeat?.Stop(); heartbeat?.Dispose(); heartbeat = new global::Tizen.NUI.Timer(120_000);
            heartbeat.Tick += (_, _) => { _ = Run(async () => await Api.KeepAliveAsync(lifetime.Token)); return true; }; heartbeat.Start();
            await ShowChannels();
        });
        status.Text = "O endereço é salvo. O PIN é solicitado a cada abertura. Use OK para editar os campos."; Select(3);
    }
    private async Task ShowChannels()
    {
        var channels = await Api.ChannelsAsync(lifetime.Token);
        Page("TV ao vivo");
        channelList = true; firstRow = 6;
        var search = new TextField { PlaceholderText = "Buscar canal por nome ou número", Position = new Position(70,240), Size = new Size(1750,72), PointSize = 28, Focusable = true };
        page!.Add(search); focus.Add((search,null));
        page.Add(new View { Position = new Position(1160,330), Size = new Size(670,600), BackgroundColor = new Color(0.14f,0.2f,0.27f,1) });
        var name = Label("",1200,350,620,130,34);
        Channel? current = null;
        Button("▶ Assistir",1200,520,620,async () => { if (current != null) Play(await Api.LiveUriAsync(current.Id,lifetime.Token),current.Name,true,current.Id); });
        Button("Guia • próximas 24 horas",1200,620,620,async () =>
        {
            if (current == null) return;
            var channel = current;
            var guide = await Api.GuideAsync(channel.Id,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(1),lifetime.Token);
            Rows("Guia • " + channel.Name,guide.Select(p => (p.ToString(),(Func<Task>)(() => ProgrammeActions(channel,p)))).ToArray(),0);
        });
        int baseFocus = focus.Count;
        InitRows(330,1050);
        void Filter()
        {
            for (int i = focus.Count - 1; i >= baseFocus; i--) { rowsContainer!.Remove(focus[i].view); focus[i].view.Dispose(); focus.RemoveAt(i); }
            rowPositions.Clear(); rowSelections.Clear(); rowOffset = 0; ScrollRows(0);
            var filtered = channels.Where(c => Presentation.Matches(c,search.Text)).ToArray();
            current = filtered.FirstOrDefault(); name.Text = current?.Name ?? "Nenhum canal encontrado";
            for (int i = 0; i < filtered.Length; i++)
            {
                var channel = filtered[i];
                var row = Button(channel.ToString(),0,i*90,1050,async () => Play(await Api.LiveUriAsync(channel.Id,lifetime.Token),channel.Name,true,channel.Id),rowsContainer);
                rowPositions[row] = i; rowSelections[row] = () => { current = channel; name.Text = channel.Name; };
            }
            status.Text = $"{filtered.Length} de {channels.Count} canais • setas e OK ou mouse";
            selected = 3;
        }
        search.TextChanged += (_,_) => Filter(); Filter();
        Select(focus.Count > baseFocus ? baseFocus : 3);
    }
    private void InitRows(int top, int width)
    {
        rowsViewport = new View { Position = new Position(70,top), Size = new Size(width,600), ClippingMode = ClippingModeType.ClipToBoundingBox };
        rowsContainer = new View { Size = new Size(width,600) }; rowsViewport.Add(rowsContainer); page!.Add(rowsViewport);
        rowsViewport.WheelEvent += (_,e) => { ScrollRows(rowOffset + e.Wheel.Z*90); return true; };
    }
    private void ScrollRows(int offset)
    {
        rowOffset = Math.Max(0,Math.Min(offset,Math.Max(0,rowPositions.Count*90-600)));
        if (rowsContainer != null) rowsContainer.Position = new Position(0,-rowOffset);
    }
    private void Rows(string title, (string text, Func<Task> action)[] rows, int offset)
    {
        Page(title); InitRows(300,1750);
        for (int i = 0; i < rows.Length; i++)
        {
            var row = Button(rows[i].text,0,i*90,1750,rows[i].action,rowsContainer);
            rowPositions[row] = i;
        }
        status.Text = rows.Length == 0 ? "Nenhum item disponível no servidor." : $"{rows.Length} itens • setas / OK ou mouse";
        Select(rows.Length > 0 ? 3 : 0);
    }
    private Task ChannelActions(Channel c)
    {
        Page(c.Name);
        Button("▶ Assistir", 70, 260, 1300, async () => Play(await Api.LiveUriAsync(c.Id, lifetime.Token), c.Name,true,c.Id));
        Button("Guia • próximas 24 horas", 70, 360, 1300, async () =>
        {
            var guide = await Api.GuideAsync(c.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), lifetime.Token);
            Rows("Guia • " + c.Name, guide.Select(p => (p.ToString(), (Func<Task>)(() => ProgrammeActions(c, p)))).ToArray(), 0);
        });
        Button("Voltar", 70, 460, 1300, ShowChannels); Select(3); return Task.CompletedTask;
    }
    private Task ProgrammeActions(Channel c, Programme p)
    {
        Page(p.Name); Label(p + "\n" + p.Description, 70, 240, 1750, 360, 26);
        Button("▶ Assistir canal", 70, 650, 700, async () => Play(await Api.LiveUriAsync(c.Id, lifetime.Token), c.Name,true,c.Id));
        Button("Gravar programa", 800, 650, 700, async () => { await Api.ScheduleAsync(p.Id, lifetime.Token); status.Text = "Gravação agendada."; });
        Button("Voltar", 70, 750, 700, () => ChannelActions(c)); Select(3); return Task.CompletedTask;
    }
    private async Task ShowRecordings()
    {
        var recordings = await Api.RecordingsAsync(lifetime.Token);
        Rows("Gravações", recordings.Select(r => (r.ToString(), (Func<Task>)(async () =>
        {
            if (!r.Playable) throw new ArgumentException("Esta gravação ainda não está disponível.");
            Play(await Api.RecordingUriAsync(r.Id, lifetime.Token), r.Name, false);
        }))).ToArray(), 0);
    }
    private void Play(Uri uri, string title, bool live = true, int? channelId = null)
    {
        Page(title, false);
        status.Hide();
        video = new VideoView { Position = new Position(0,0), Size = new Size(1920,1080), ResourceUrl = uri.AbsoluteUri };
        page!.Add(video); video.Play(); playing = true;
        liveChannels = live ? new LiveChannelNavigator(Api, channelId, title) : null;
        video.HoverEvent += (_,_) => { RevealControls(); return false; };
        video.TouchEvent += (_,_) => { RevealControls(); return false; };
        controls = new View { Position = new Position(50,810), Size = new Size(1820,220), BackgroundColor = new Color(0.06f,0.09f,0.13f,0.7f) };
        page.Add(controls);
        feedName = new TextLabel { Text = title, Position = new Position(30,15), Size = new Size(1750,65), PointSize = 30, TextColor = Color.White }; controls.Add(feedName);
        page.Remove(status); controls.Add(status); status.Position = new Position(30,75); status.Size = new Size(1750,30); status.PointSize = 18; status.Show();
        Button("Voltar",30,105,400,ShowChannels,controls);
        if (live) { Button("Canal −",450,105,400,() => { _ = SwitchChannel(-1); return Task.CompletedTask; },controls); Button("Canal +",870,105,400,() => { _ = SwitchChannel(1); return Task.CompletedTask; },controls); }
        if (!live)
        {
            Button("−30s",450,105,260,() => { video?.Backward(30000); return Task.CompletedTask; },controls);
            Button("Pausar / Play",730,105,500,() => { if (video != null) { if (paused) video.Play(); else video.Pause(); paused = !paused; } return Task.CompletedTask; },controls);
            Button("+30s",1250,105,260,() => { video?.Forward(30000); return Task.CompletedTask; },controls);
        }
        hideControls = new global::Tizen.NUI.Timer((uint)Presentation.ControlsTimeoutMs);
        hideControls.Tick += (_,_) => { controls?.Hide(); FocusManager.Instance.ClearFocus(); return false; };
        Select(0); RevealControls();
    }
    private bool paused;
    private async Task SwitchChannel(int direction)
    {
        var navigator = liveChannels;
        if (navigator == null) return;
        try
        {
            var feed = await navigator.SwitchAsync(direction,lifetime.Token);
            if (feed == null || !playing || liveChannels != navigator || video == null) return;
            video.Stop(); video.ResourceUrl = feed.Uri.AbsoluteUri; video.Play(); paused = false;
            if (feedName != null) feedName.Text = feed.Channel.ToString(); status.Text = ""; RevealControls();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (playing && liveChannels == navigator) { status.Text = "Não foi possível trocar de canal. Tente novamente."; RevealControls(); } }
    }
    private void RevealControls()
    {
        if (!playing || controls == null) return;
        controls.Show(); hideControls?.Stop(); hideControls?.Start();
    }
    private void StopVideo()
    {
        playing = false; paused = false; suppressClick = false;
        liveChannels = null; feedName = null;
        hideControls?.Stop(); hideControls?.Dispose(); hideControls = null; controls = null;
        if (video != null) { video.Stop(); video.Dispose(); video = null; }
    }
    protected override void OnPause() { StopVideo(); base.OnPause(); }
    protected override void OnTerminate() { lifetime.Cancel(); StopVideo(); heartbeat?.Stop(); heartbeat?.Dispose(); client?.Dispose(); lifetime.Dispose(); base.OnTerminate(); }
}


