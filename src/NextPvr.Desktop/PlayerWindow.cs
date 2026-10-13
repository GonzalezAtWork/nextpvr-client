using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
using NextPvr.Core;

namespace NextPvr.Desktop;

public sealed class PlayerWindow : Window
{
    private readonly LibVLC vlc;
    private readonly MediaPlayer player;
    private readonly DispatcherTimer hide = new() { Interval = TimeSpan.FromMilliseconds(Presentation.ControlsTimeoutMs) };
    private readonly Border controls;
    private readonly StackPanel bar;
    private readonly bool live;
    private bool closed;
    private readonly LiveChannelNavigator? channels;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBlock channelName;
    private readonly TextBlock state;
    private void Reveal() { if (closed) return; controls.IsVisible = true; hide.Stop(); hide.Start(); }
    public PlayerWindow(Uri uri, string title, bool live, LiveChannelNavigator? channels = null)
    {
        this.live = live;
        this.channels = channels;
        Title = title; Width = 1280; Height = 760; WindowState = WindowState.FullScreen;
        Background = Brushes.Black;
        LibVLCSharp.Shared.Core.Initialize(); vlc = new LibVLC("--no-video-title-show", "--network-caching=1500", "--http-reconnect"); player = new MediaPlayer(vlc);
        bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        channelName = new TextBlock { Text = title, FontSize = 22, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        state = new TextBlock { Text = "Abrindo…", FontSize = 16, Foreground = Brushes.LightGray };
        Button Add(string text, Action action) { var b = new Button { Content = text, MinHeight = 54, Background = new SolidColorBrush(Color.Parse("#99243344")) }; b.Click += (_, _) => { action(); Reveal(); }; bar.Children.Add(b); return b; }
        var back = Add("Voltar", Close);
        if (live && channels != null) { Add("Canal −", () => { _ = SwitchChannel(-1); }); Add("Canal +", () => { _ = SwitchChannel(1); }); }
        if (!live) { Add("−30s", () => Seek(-30)); Add("Pausar / Play", () => player.Pause()); Add("+30s", () => Seek(30)); }
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(channelName); panel.Children.Add(state); panel.Children.Add(bar);
        controls = new Border { Background = new SolidColorBrush(Color.Parse("#B3101822")), Padding = new Thickness(32), Margin = new Thickness(32), CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Bottom, Child = panel };
        var overlay = new Grid { Background = Brushes.Transparent, Focusable = true }; overlay.Children.Add(controls);
        // Content is rendered above the native VLC surface by VideoView's overlay window.
        var video = new VideoView { MediaPlayer = player, Content = overlay }; Content = video;
        overlay.PointerMoved += (_, _) => Reveal(); overlay.PointerPressed += (_, _) => Reveal(); overlay.PointerWheelChanged += (_, _) => Reveal();
        overlay.AddHandler(KeyDownEvent, RemoteKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, RemoteKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        hide.Tick += (_, _) => { controls.IsVisible = false; overlay.Focus(); hide.Stop(); };
        player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() => { state.Text = "Falha ao reproduzir. Verifique o stream e os codecs."; Reveal(); });
        player.Playing += (_, _) => Dispatcher.UIThread.Post(() => state.Text = "");
        player.EndReached += (_, _) => Dispatcher.UIThread.Post(() => { state.Text = "Reprodução encerrada."; Reveal(); });
        Opened += (_, _) => { using var media = new Media(vlc, uri); player.Play(media); back.Focus(); Reveal(); };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); hide.Stop(); video.MediaPlayer = null; player.Stop(); player.Dispose(); vlc.Dispose(); };
    }
    private void RemoteKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        bool hidden = !controls.IsVisible; Reveal();
        if (e.Key is Key.Escape or Key.BrowserBack or Key.MediaStop) Close();
        else if (live && channels != null && (e.Key is Key.PageUp or Key.MediaNextTrack || e.KeySymbol is "XF86RaiseChannel" or "XF86ChannelUp" or "ChannelUp")) { _ = SwitchChannel(1); }
        else if (live && channels != null && (e.Key is Key.PageDown or Key.MediaPreviousTrack || e.KeySymbol is "XF86LowerChannel" or "XF86ChannelDown" or "ChannelDown")) { _ = SwitchChannel(-1); }
        else if (hidden && e.Key is Key.Enter or Key.Left or Key.Right or Key.Up or Key.Down) { bar.Children.OfType<Button>().First().Focus(); }
        else if (e.Key is Key.Space or Key.MediaPlayPause && !live) player.Pause();
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) RemoteFocus.Move(bar, e.Key is Key.Left or Key.Up);
        else return;
        e.Handled = true;
    }
    private async Task SwitchChannel(int direction)
    {
        if (channels == null || closed) return;
        try
        {
            var feed = await channels.SwitchAsync(direction, lifetime.Token);
            if (feed == null || closed) return;
            Title = feed.Channel.Name; channelName.Text = feed.Channel.ToString(); state.Text = "Abrindo…";
            player.Stop(); using var media = new Media(vlc, feed.Uri); player.Play(media); Reveal();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) { state.Text = "Não foi possível trocar de canal. Tente novamente."; Reveal(); } }
    }
    private void Seek(int seconds) { if (player.IsSeekable) player.Time = Math.Max(0, player.Time + seconds * 1000L); }
}
