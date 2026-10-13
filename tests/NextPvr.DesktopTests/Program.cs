using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using NextPvr.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(); window.Show();
        var fields = window.GetVisualDescendants().OfType<TextBox>().ToArray();
        if (fields.Length != 2) throw new Exception("Connection form must contain server and PIN");
        fields[0].Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        if (!fields[1].IsFocused) throw new Exception("Remote Down must move from server to PIN");
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        var connect = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Conectar");
        if (!connect.IsFocused) throw new Exception("Remote Down must reach Connect");
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        if (!fields[1].IsFocused) throw new Exception("Remote Up must return to PIN");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        if (!connect.IsFocused) throw new Exception("Remote Enter must leave PIN editing");
        if (window.CaptureRenderedFrame() is { } frame)
        {
            Directory.CreateDirectory("artifacts"); frame.Save("artifacts/desktop-preview.png"); frame.Dispose();
        }
        VerifyChannels(window);
        window.Close();
        Console.WriteLine("PASS: Native desktop form, remote Down/Up/Enter navigation and shutdown");
        if (!args.Contains("--ui-only")) VerifyEmbeddedPlayer();
    }
    private static void VerifyChannels(MainWindow window)
    {
        var api = new NextPvr.Core.NextPvrClient("http://nextpvr.test:8866", "0000", new ChannelHandler());
        typeof(MainWindow).GetField("client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, api);
        var task = (Task)typeof(MainWindow).GetMethod("ShowChannels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
        task.GetAwaiter().GetResult(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        var search = window.GetVisualDescendants().OfType<TextBox>().Single();
        if (list.ItemCount != 100) throw new Exception("All channels must be available without pagination");
        search.Text = "NOTICIAS"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (list.ItemCount != 1) throw new Exception("Channel search must ignore case and accents");
        search.Text = "99"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (list.ItemCount != 1 || ((NextPvr.Core.Channel)list.Items[0]!).Number != "99") throw new Exception("Channel search must match number");
        search.Text = "missing"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (list.ItemCount != 0) throw new Exception("Search must allow no matches");
        search.Text = ""; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); search.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        if (!list.IsKeyboardFocusWithin) throw new Exception("Remote must reach channel list from search: " + window.FocusManager?.GetFocusedElement()?.GetType().Name + " / " + string.Join(",",window.GetVisualDescendants().OfType<Control>().Where(c => c is Button or TextBox or ListBox).Select(c => c.GetType().Name)));
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        if (list.SelectedIndex != 1) throw new Exception("Remote Down must select the next channel");
        list.SelectedIndex = 99; list.ScrollIntoView(list.SelectedItem!); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (list.SelectedIndex != 99) throw new Exception("Last channel must remain selectable");
        list.SelectedIndex = 0; list.ScrollIntoView(list.SelectedItem!); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (window.CaptureRenderedFrame() is { } frame) { frame.Save("artifacts/channels-preview.png"); frame.Dispose(); }
        Console.WriteLine("PASS: Continuous 100-channel list, name/number/accent search, empty results and remote focus");
    }
    private sealed class ChannelHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = request.RequestUri!.Query;
            var xml = query.Contains("session.initiate") ? "<sid>test</sid><salt>test</salt>" : query.Contains("channel.list")
                ? "<channels>" + string.Concat(Enumerable.Range(1,100).Select(i => $"<channel><id>{i}</id><number>{i}</number><name>{(i == 1 ? "TV Notícias" : "Canal " + i)}</name></channel>")) + "</channels>" : "";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("<rsp stat='ok'>" + xml + "</rsp>") });
        }
    }
    private static void VerifyEmbeddedPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        using var vlc = new LibVLCSharp.Shared.LibVLC("--aout=dummy", "--vout=dummy", "--no-video-title-show");
        using var player = new LibVLCSharp.Shared.MediaPlayer(vlc);
        using var played = new ManualResetEventSlim();
        player.Playing += (_, _) => played.Set();
        using var pcm = new MemoryStream();
        using (var writer = new BinaryWriter(pcm, System.Text.Encoding.ASCII, true))
        {
            const int sampleRate = 8000, length = sampleRate * 2 * 2;
            writer.Write("RIFF"u8); writer.Write(36 + length); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
            writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(length); writer.Write(new byte[length]);
        }
        pcm.Position = 0;
        using var input = new LibVLCSharp.Shared.StreamMediaInput(pcm);
        using var media = new LibVLCSharp.Shared.Media(vlc, input);
        if (!player.Play(media) || !played.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Embedded VLC failed to start PCM stream");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (media.Statistics.DecodedAudio == 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);
        if (media.Statistics.DecodedAudio == 0) throw new Exception("Embedded VLC did not decode audio");
        player.Stop();
        Console.WriteLine("PASS: In-process libVLC loads and decodes a memory stream");
    }
}

