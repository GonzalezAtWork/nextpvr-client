using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using LibVLCSharp.Shared;
using VideoView = LibVLCSharp.Platforms.Android.VideoView;
using Orientation = Android.Widget.Orientation;
using NextPvr.Core;
using Presentation = NextPvr.Core.Presentation;

namespace NextPvr.Android;

[Activity(Label = "NextPVR • Player", Exported = false, ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize)]
public sealed class PlayerActivity : Activity
{
    private LibVLC? vlc;
    private MediaPlayer? player;
    private VideoView? video;
    private LinearLayout controls = null!;
    private Button back = null!;
    private readonly Handler hideHandler = new(Looper.MainLooper!);
    private Action hide = null!;
    private bool live;
    private bool resumePlayback;
    private LiveChannelNavigator? channels;
    private readonly CancellationTokenSource lifetime = new();
    private TextView channelName = null!;
    private TextView status = null!;
    private void Reveal() { if (lifetime.IsCancellationRequested) return; controls.Visibility = ViewStates.Visible; hideHandler.RemoveCallbacks(hide); hideHandler.PostDelayed(hide, Presentation.ControlsTimeoutMs); }
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState); Window?.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
        Window?.RequestFeature(WindowFeatures.NoTitle);
#pragma warning disable CA1422
        if (Window != null) Window.DecorView.SystemUiFlags = SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable;
#pragma warning restore CA1422
        live = Intent?.GetBooleanExtra("live", false) ?? false;
        channels = live ? PlaybackSession.LiveChannels : null;
        LibVLCSharp.Shared.Core.Initialize(); vlc = new LibVLC("--no-video-title-show", "--network-caching=1500", "--http-reconnect"); player = new MediaPlayer(vlc);
        var root = new FrameLayout(this); root.SetBackgroundColor(global::Android.Graphics.Color.Black);
        video = new VideoView(this) { MediaPlayer = player }; root.AddView(video, new FrameLayout.LayoutParams(-1, -1));
        controls = new LinearLayout(this) { Orientation = Orientation.Vertical }; controls.SetPadding(32, 24, 32, 24); controls.SetBackgroundColor(global::Android.Graphics.Color.Argb(179,16,24,34));
        channelName = new TextView(this) { Text = Intent?.GetStringExtra("title"), TextSize = 22 }; channelName.SetTextColor(global::Android.Graphics.Color.White); controls.AddView(channelName);
        status = new TextView(this) { TextSize = 16 }; status.SetTextColor(global::Android.Graphics.Color.White); controls.AddView(status);
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal }; controls.AddView(bar);
        Button Add(string text, Action action) { var b = new Button(this) { Text = text, Focusable = true, TextSize = 18 }; b.SetTextColor(global::Android.Graphics.Color.White); b.BackgroundTintList = new global::Android.Content.Res.ColorStateList(new[] { new[] { global::Android.Resource.Attribute.StateFocused }, Array.Empty<int>() }, new[] { global::Android.Graphics.Color.Argb(190,22,184,212).ToArgb(), global::Android.Graphics.Color.Argb(153,36,51,68).ToArgb() }); b.Click += (_, _) => { action(); Reveal(); }; bar.AddView(b); return b; }
        back = Add("Voltar", Finish);
        if (live && channels != null) { Add("Canal −", () => { _ = SwitchChannel(-1); }); Add("Canal +", () => { _ = SwitchChannel(1); }); }
        if (!live) { Add("−30s", () => Seek(-30)); Add("Pausar / Play", () => player.Pause()); Add("+30s", () => Seek(30)); }
        var layout = new FrameLayout.LayoutParams(-1, -2, GravityFlags.Bottom); layout.SetMargins(32,32,32,32); root.AddView(controls,layout); SetContentView(root);
        hide = () => controls.Visibility = ViewStates.Invisible;
        player.EncounteredError += (_, _) => RunOnUiThread(() => { status.Text = "Falha ao reproduzir stream."; Reveal(); });
        using var media = new Media(vlc, new Uri(Intent?.GetStringExtra("url") ?? throw new InvalidOperationException())); player.Play(media); back.RequestFocus(); Reveal();
    }
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e?.Action == KeyEventActions.Down)
        {
            bool hidden = controls.Visibility != ViewStates.Visible; Reveal();
            if (hidden && e.KeyCode is Keycode.DpadCenter or Keycode.Enter or Keycode.DpadLeft or Keycode.DpadRight or Keycode.DpadUp or Keycode.DpadDown) { back.RequestFocus(); return true; }
        }
        return base.DispatchKeyEvent(e);
    }
    public override bool DispatchTouchEvent(MotionEvent? e) { Reveal(); return base.DispatchTouchEvent(e); }
    public override bool DispatchGenericMotionEvent(MotionEvent? e) { Reveal(); return base.DispatchGenericMotionEvent(e); }
    private void Seek(int seconds) { if (player?.IsSeekable == true) player.Time = Math.Max(0, player.Time + seconds * 1000L); }
    private async Task SwitchChannel(int direction)
    {
        if (channels == null) return;
        try
        {
            var feed = await channels.SwitchAsync(direction, lifetime.Token);
            if (feed == null || lifetime.IsCancellationRequested) return;
            channelName.Text = feed.Channel.ToString(); status.Text = "";
            player?.Stop(); using var media = new Media(vlc!, feed.Uri); player?.Play(media); Reveal();
        }
        catch (System.OperationCanceledException) { }
        catch (Exception) { if (!lifetime.IsCancellationRequested) { status.Text = "Não foi possível trocar de canal. Tente novamente."; Reveal(); } }
    }
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (live && channels != null && keyCode is Keycode.ChannelUp or Keycode.ChannelDown or Keycode.PageUp or Keycode.PageDown) { _ = SwitchChannel(keyCode is Keycode.ChannelUp or Keycode.PageUp ? 1 : -1); return true; }
        if (keyCode is Keycode.MediaPlayPause or Keycode.MediaPause && !live) { player?.Pause(); return true; }
        if (keyCode == Keycode.MediaStop) { Finish(); return true; }
        if (keyCode == Keycode.MediaRewind) { Seek(-30); return true; }
        if (keyCode == Keycode.MediaFastForward) { Seek(30); return true; }
        return base.OnKeyDown(keyCode, e);
    }
    protected override void OnPause() { hideHandler.RemoveCallbacks(hide); resumePlayback = player?.IsPlaying == true; if (live) player?.Stop(); else player?.SetPause(true); base.OnPause(); }
    protected override void OnResume() { base.OnResume(); if (resumePlayback) player?.Play(); if (hide != null) Reveal(); }
    protected override void OnDestroy() { lifetime.Cancel(); if (ReferenceEquals(PlaybackSession.LiveChannels, channels)) PlaybackSession.LiveChannels = null; hideHandler.RemoveCallbacksAndMessages(null); hideHandler.Dispose(); if (video != null) video.MediaPlayer = null; player?.Stop(); player?.Dispose(); vlc?.Dispose(); base.OnDestroy(); }
}
