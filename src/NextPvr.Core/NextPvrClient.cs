using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NextPvr.Core;

/// <summary>Only communicates with the configured NextPVR server. No proxy or local service.</summary>
public sealed class NextPvrClient : IDisposable
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim sessionLock = new(1, 1);
    private readonly string pin;
    private string? sid;
    private DateTimeOffset lastRenewal;
    public Uri Server { get; }

    public NextPvrClient(string server, string pin, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(server.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Informe um endereço HTTP/HTTPS válido, por exemplo http://192.168.1.10:8866.");
        Server = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        this.pin = pin;
        http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await sessionLock.WaitAsync(ct);
        try { await LoginAsync(ct); }
        finally { sessionLock.Release(); }
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        sid = null;
        var initiate = await RequestAsync("session.initiate", new() { ["ver"] = "1.0", ["device"] = "NextPvrClient" }, false, ct);
        string candidate = Required(initiate, "sid");
        var digest = Hash(":" + Hash(pin) + ":" + Required(initiate, "salt"));
        await RequestAsync("session.login", new() { ["sid"] = candidate, ["md5"] = digest }, false, ct);
        sid = candidate;
        lastRenewal = DateTimeOffset.UtcNow;
    }

    public static string Hash(string value)
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
#else
        using var md5 = MD5.Create();
        return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
#endif
    }

    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        await sessionLock.WaitAsync(ct);
        try
        {
            if (sid == null) await LoginAsync(ct);
            else if (DateTimeOffset.UtcNow - lastRenewal > TimeSpan.FromMinutes(3))
            {
                try { await RequestAsync("session.valid", new(), true, ct); }
                catch (NextPvrException ex) when (ex.Code == 8) { await LoginAsync(ct); }
            }
        }
        finally { sessionLock.Release(); }
    }

    private async Task<XElement> CallAsync(string method, Dictionary<string, string>? args = null, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);
        try { return await RequestAsync(method, args ?? new(), true, ct); }
        catch (NextPvrException ex) when (ex.Code == 8)
        {
            await ConnectAsync(ct);
            return await RequestAsync(method, args ?? new(), true, ct);
        }
    }

    private Uri BuildUri(string path, Dictionary<string, string> args) =>
        new(Server, path + "?" + string.Join("&", args.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));

    private async Task<XElement> RequestAsync(string method, Dictionary<string, string> args, bool authenticated, CancellationToken ct)
    {
        var query = new Dictionary<string, string>(args) { ["method"] = method };
        if (authenticated) query["sid"] = sid ?? throw new InvalidOperationException("Sessão não iniciada.");
        using var response = await http.GetAsync(BuildUri("service", query), ct);
        if (!response.IsSuccessStatusCode)
            throw new NextPvrException($"Servidor respondeu HTTP {(int)response.StatusCode}. Verifique endereço e acesso remoto.");
#if NET8_0_OR_GREATER
        using var stream = await response.Content.ReadAsStreamAsync(ct);
#else
        using var stream = await response.Content.ReadAsStreamAsync();
        ct.ThrowIfCancellationRequested();
#endif
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 16_000_000 });
        var doc = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        var root = doc.Root ?? throw new NextPvrException("Resposta vazia do NextPVR.");
        if ((string?)root.Attribute("stat") != "ok")
        {
            var error = root.Element("err");
            int.TryParse((string?)error?.Attribute("code"), out var code);
            // Do not echo URLs, session tokens or server-supplied diagnostics containing secrets.
            throw new NextPvrException(code == 8 ? "Sessão expirada." : "NextPVR recusou a operação. Verifique o PIN e a configuração de acesso do servidor.", code);
        }
        if (authenticated) lastRenewal = DateTimeOffset.UtcNow;
        return root;
    }

    public async Task<IReadOnlyList<Channel>> ChannelsAsync(CancellationToken ct = default)
    {
        var root = await CallAsync("channel.list", new() { ["extras"] = "true" }, ct);
        return root.Descendants("channel").Select(e => new Channel(Int(e, "id"), Text(e, "name"),
            Text(e, "number") + (Int(e, "minor") > 0 ? "." + Text(e, "minor") : ""), Text(e, "type") == "0xa" || Text(e, "type").Equals("radio", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => double.TryParse(c.Number, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : double.MaxValue).ToArray();
    }

    public async Task<IReadOnlyList<Programme>> GuideAsync(int channelId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var root = await CallAsync("channel.listings", new() { ["channel_id"] = channelId.ToString(CultureInfo.InvariantCulture),
            ["start"] = start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ["end"] = end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) }, ct);
        return root.Descendants("l").Select(e => new Programme(Int(e, "id"), Text(e, "name"), Text(e, "description"), Time(e, "start"), Time(e, "end")))
            .OrderBy(p => p.Start).ToArray();
    }

    public async Task<IReadOnlyList<Recording>> RecordingsAsync(CancellationToken ct = default)
    {
        var root = await CallAsync("recording.list", new() { ["filter"] = "all" }, ct);
        return root.Descendants("recording").Select(e => new Recording(Int(e, "id"), Text(e, "name"), Text(e, "desc"),
            Text(e, "status"), Time(e, "start_time_ticks"), Int(e, "duration_seconds"))).OrderByDescending(r => r.Start).ToArray();
    }

    public async Task ScheduleAsync(int eventId, CancellationToken ct = default)
    {
        if (eventId <= 0) throw new ArgumentException("Programa sem identificador de EPG.");
        await CallAsync("recording.save", new() { ["recording_id"] = "0", ["event_id"] = eventId.ToString(CultureInfo.InvariantCulture),
            ["pre_padding"] = "1", ["post_padding"] = "2", ["directory_id"] = "Default" }, ct);
    }

    public async Task<Uri> LiveUriAsync(int channelId, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);
        return BuildUri("live", new() { ["channeloid"] = channelId.ToString(CultureInfo.InvariantCulture), ["client"] = "NextPvrClient-" + sid, ["sid"] = sid! });
    }
    public async Task<Uri> RecordingUriAsync(int recordingId, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);
        return BuildUri("live", new() { ["recording"] = recordingId.ToString(CultureInfo.InvariantCulture), ["client"] = "NextPvrClient-" + sid, ["sid"] = sid! });
    }
    public Task KeepAliveAsync(CancellationToken ct = default) => CallAsync("session.valid", ct: ct);
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (sid == null) return;
        try { await RequestAsync("session.logout", new(), true, ct); }
        finally { sid = null; }
    }
    public void Dispose() { http.Dispose(); sessionLock.Dispose(); }

    private static string Text(XElement e, string key) => (string?)e.Element(key) ?? "";
    private static string Required(XElement e, string key) => string.IsNullOrWhiteSpace(Text(e, key)) ? throw new NextPvrException("Resposta de autenticação incompleta.") : Text(e, key);
    private static int Int(XElement e, string key) => int.TryParse(Text(e, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
    private static DateTimeOffset Time(XElement e, string key)
    {
        var raw = Text(e, key);
        if (long.TryParse(raw, out var value)) return value > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt : DateTimeOffset.UnixEpoch;
    }
}
