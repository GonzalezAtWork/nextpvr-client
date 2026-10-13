using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using NextPvr.Core;
using Presentation = NextPvr.Core.Presentation;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;

namespace NextPvr.Android;

[Activity(Label = "NextPVR Sala", MainLauncher = true, Exported = true, ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize)]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLeanbackLauncher])]
public sealed class MainActivity : Activity
{
    private LinearLayout root = null!;
    private TextView status = null!;
    private NextPvrClient? client;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? heartbeat;
    private bool busy;
    private bool isHome;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState); Window?.AddFlags(WindowManagerFlags.KeepScreenOn); ShowSettings();
    }
    private void Page(string title)
    {
        isHome = false;
        root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 24, 32, 20); root.SetBackgroundColor(Color.Rgb(16, 24, 34));
        var nav = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var brand = new TextView(this) { Text = "NextPVR", TextSize = 30 }; brand.SetTextColor(Color.ParseColor(Presentation.Accent)); nav.AddView(brand);
        nav.AddView(Button("TV ao vivo", ShowChannels)); nav.AddView(Button("Gravações", ShowRecordings));
        nav.AddView(Button("Configurações", () => { ShowSettings(); return Task.CompletedTask; })); root.AddView(nav);
        root.AddView(new TextView(this) { Text = title, TextSize = 30 });
        status = new TextView(this) { TextSize = 16 }; root.AddView(status); SetContentView(root);
    }
    private Button Button(string title, Func<Task> action)
    {
        var b = new Button(this) { Text = title, TextSize = 18, Focusable = true };
        b.SetMinHeight(56);
        b.SetTextColor(Color.White);
        b.BackgroundTintList = new global::Android.Content.Res.ColorStateList(
            new[] { new[] { global::Android.Resource.Attribute.StateFocused }, Array.Empty<int>() },
            new[] { Color.ParseColor(Presentation.Accent).ToArgb(), Color.ParseColor(Presentation.Surface).ToArgb() });
        b.Click += async (_, _) => await Run(action); return b;
    }
    private void AddRows(IEnumerable<(string title, Func<Task> action)> items)
    {
        var scroll = new ScrollView(this); var rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        foreach (var (title, action) in items) rows.AddView(Button(title, action));
        scroll.AddView(rows); root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        if (rows.ChildCount > 0) rows.GetChildAt(0)?.RequestFocus();
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true;
        try { await action(); }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) status.Text = ex is NextPvrException or ArgumentException ? ex.Message : "Falha na conexão. Verifique servidor e rede."; }
        finally { busy = false; }
    }
    private NextPvrClient Api => client ?? throw new ArgumentException("Conecte seu servidor em Configurações.");
    private void ShowSettings()
    {
        Page("NextPVR • Conectar");
        var prefs = GetSharedPreferences("server", FileCreationMode.Private)!;
        var server = new EditText(this) { Text = prefs.GetString("address", "http://192.168.1.10:8866"), Hint = "Endereço do servidor", TextSize = 22 };
        var pin = new EditText(this) { Hint = "PIN de acesso remoto", TextSize = 22, InputType = global::Android.Text.InputTypes.ClassNumber | global::Android.Text.InputTypes.NumberVariationPassword };
        server.SetSingleLine(true); pin.SetSingleLine(true);
        root.AddView(server); root.AddView(pin); root.AddView(Button("Conectar", async () =>
        {
            var candidate = new NextPvrClient(server.Text ?? "", pin.Text ?? "");
            try { await candidate.ConnectAsync(lifetime.Token); } catch { candidate.Dispose(); throw; }
            client?.Dispose(); client = candidate;
            prefs.Edit()!.PutString("address", candidate.Server.AbsoluteUri)!.Apply();
            StartHeartbeat(); await ShowChannels();
        }));
        status.Text = "O endereço será salvo. O PIN será solicitado a cada abertura."; server.RequestFocus();
    }
    private async Task ShowChannels()
    {
        var channels = await Api.ChannelsAsync(lifetime.Token); Page("TV ao vivo"); isHome = true;
        var search = new EditText(this) { Id = View.GenerateViewId(), Hint = "Buscar canal por nome ou número", TextSize = 22 }; search.SetSingleLine(true); root.AddView(search);
        var content = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var scroll = new ScrollView(this); var rows = new LinearLayout(this) { Orientation = Orientation.Vertical }; scroll.AddView(rows);
        var details = new LinearLayout(this) { Orientation = Orientation.Vertical }; details.SetPadding(24,24,24,24); details.SetBackgroundColor(Color.ParseColor(Presentation.Surface));
        content.AddView(scroll, new LinearLayout.LayoutParams(0,-1,3)); content.AddView(details,new LinearLayout.LayoutParams(0,-1,2));
        root.AddView(content,new LinearLayout.LayoutParams(-1,0,1));
        void Select(Channel channel, Button? row = null)
        {
            details.RemoveAllViews(); details.AddView(new TextView(this) { Text = channel.Name, TextSize = 28 });
            var watch = Button("▶ Assistir",async () => Play(await Api.LiveUriAsync(channel.Id,lifetime.Token),channel.Name,true,channel.Id)); watch.Id = View.GenerateViewId(); details.AddView(watch);
            if (row != null) { row.NextFocusRightId = watch.Id; watch.NextFocusLeftId = row.Id; }
            details.AddView(Button("Guia • próximas 24 horas",async () =>
            {
                var guide = await Api.GuideAsync(channel.Id,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(1),lifetime.Token);
                Page("Guia • " + channel.Name); AddRows(guide.Select(p => (p.ToString(),(Func<Task>)(() => ProgrammeActions(channel,p)))));
            }));
        }
        void Filter()
        {
            rows.RemoveAllViews(); details.RemoveAllViews();
            var filtered = channels.Where(c => Presentation.Matches(c,search.Text)).ToArray();
            foreach (var channel in filtered)
            {
                var button = Button(channel.ToString(), async () => Play(await Api.LiveUriAsync(channel.Id,lifetime.Token),channel.Name,true,channel.Id));
                button.Id = View.GenerateViewId();
                button.FocusChange += (_,e) => { if (e.HasFocus) Select(channel,button); }; rows.AddView(button);
            }
            if (filtered.Length > 0) { Select(filtered[0]); search.NextFocusDownId = rows.GetChildAt(0)!.Id; rows.GetChildAt(0)!.NextFocusUpId = search.Id; }
            else details.AddView(new TextView(this) { Text = "Nenhum canal encontrado", TextSize = 22 });
            status.Text = $"{filtered.Length} de {channels.Count} canais • setas e OK ou mouse";
        }
        search.TextChanged += (_,_) => Filter(); Filter(); rows.GetChildAt(0)?.RequestFocus();
        status.Text = channels.Count == 0 ? "Nenhum canal no servidor." : "Selecione um canal • setas e OK ou mouse";
    }
    private Task ChannelActions(Channel channel)
    {
        Page(channel.Name);
        root.AddView(Button("▶ Assistir", async () => Play(await Api.LiveUriAsync(channel.Id, lifetime.Token), channel.Name, true, channel.Id)));
        root.AddView(Button("Guia • próximas 24 horas", async () =>
        {
            var guide = await Api.GuideAsync(channel.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), lifetime.Token);
            Page("Guia • " + channel.Name);
            AddRows(guide.Select(p => (p.ToString(), (Func<Task>)(() => ProgrammeActions(channel, p)))));
            status.Text = guide.Count == 0 ? "Sem programação. Atualize o EPG no servidor." : "Escolha um programa.";
        }));
        root.AddView(Button("Voltar", ShowChannels)); root.GetChildAt(3)?.RequestFocus(); return Task.CompletedTask;
    }
    private Task ProgrammeActions(Channel channel, Programme p)
    {
        Page(p.Name);
        root.AddView(new TextView(this) { Text = p + "\n" + p.Description, TextSize = 20 });
        root.AddView(Button("▶ Assistir canal", async () => Play(await Api.LiveUriAsync(channel.Id, lifetime.Token), channel.Name, true, channel.Id)));
        root.AddView(Button("Gravar programa", async () => { await Api.ScheduleAsync(p.Id, lifetime.Token); status.Text = "Gravação agendada."; }));
        root.AddView(Button("Voltar", () => ChannelActions(channel))); return Task.CompletedTask;
    }
    private async Task ShowRecordings()
    {
        var recordings = await Api.RecordingsAsync(lifetime.Token); Page("Gravações");
        AddRows(recordings.Select(r => (r.ToString(), (Func<Task>)(async () =>
        {
            if (!r.Playable) throw new ArgumentException("Gravação ainda não disponível.");
            Play(await Api.RecordingUriAsync(r.Id, lifetime.Token), r.Name, false);
        })))); status.Text = recordings.Count == 0 ? "Nenhuma gravação." : "Selecione uma gravação para reproduzir.";
    }
    private void Play(Uri uri, string title, bool live, int? channelId = null)
    {
        PlaybackSession.LiveChannels = live ? new LiveChannelNavigator(Api, channelId, title) : null;
        var intent = new Intent(this, typeof(PlayerActivity)); intent.PutExtra("url", uri.AbsoluteUri); intent.PutExtra("title", title); intent.PutExtra("live", live); StartActivity(intent);
    }
    private void StartHeartbeat()
    {
        heartbeat?.Cancel(); heartbeat?.Dispose(); heartbeat = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = HeartbeatLoop(heartbeat.Token);
    }
    private async Task HeartbeatLoop(CancellationToken ct)
    {
        try { while (true) { await Task.Delay(TimeSpan.FromMinutes(2), ct); try { await Api.KeepAliveAsync(ct); } catch when (!ct.IsCancellationRequested) { RunOnUiThread(() => status.Text = "Conexão interrompida. Tente conectar novamente."); } } }
        catch (System.OperationCanceledException) { }
    }
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back && client != null && !isHome) { _ = Run(ShowChannels); return true; }
        return base.OnKeyDown(keyCode, e);
    }
    protected override void OnDestroy() { lifetime.Cancel(); heartbeat?.Cancel(); heartbeat?.Dispose(); client?.Dispose(); lifetime.Dispose(); base.OnDestroy(); }
}

// In-process handoff between native activities; no credentials in the Intent.
internal static class PlaybackSession
{
    internal static LiveChannelNavigator? LiveChannels;
}

