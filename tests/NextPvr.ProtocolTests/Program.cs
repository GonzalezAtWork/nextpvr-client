using System.Net;
using System.Text;
using NextPvr.Core;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
var handler = new ProtocolHandler();
using var client = new NextPvrClient("http://nextpvr.test:8866/", "0000", handler);
await client.ConnectAsync();
Check(handler.Logins == 1 && handler.Authenticated, "PIN challenge/response, SID propagation");
Check(NextPvrClient.Hash("0000") == "4a7d1ed414474e4033ac29ccb8653d9b", "MD5 protocol vector");
var channels = await client.ChannelsAsync();
Check(channels.Count == 2 && channels[0].Id == 2 && channels[1].Number == "10.2" && channels[0].Radio, "channel parsing, radio type and numeric ordering");
Check(channels[1].Name == "TV & Notícias", "XML entities and Unicode");
var start = DateTimeOffset.FromUnixTimeSeconds(1800000000);
var guide = await client.GuideAsync(2, start, start.AddDays(1));
Check(guide[0].Start == start && guide[0].End == start.AddHours(1), "EPG Unix milliseconds and seconds");
Check(handler.LastStart == "1800000000", "EPG request uses Unix seconds");
await client.ScheduleAsync(guide[0].Id);
Check(handler.ScheduledId == "99", "recording.save event_id and padding");
var recordings = await client.RecordingsAsync();
Check(recordings.Count == 2 && recordings[0].Playable && !recordings[1].Playable, "recording metadata, availability and sorting");
var live = await client.LiveUriAsync(2);
Check(live.Host == "nextpvr.test" && live.AbsolutePath == "/live" && live.Query.Contains("channeloid=2") && live.Query.Contains("sid="), "direct authenticated live stream");
var recorded = await client.RecordingUriAsync(12);
Check(recorded.Query.Contains("recording=12"), "direct recording stream");
var navigator = new LiveChannelNavigator(client, 2, "Radio");
var nextFeed = await navigator.SwitchAsync(1);
Check(nextFeed?.Channel.Id == 1 && nextFeed.Uri.Query.Contains("channeloid=1"), "Channel+ opens the next feed with its name and authenticated URI");
Check((await navigator.SwitchAsync(1))?.Channel.Id == 2, "Channel+ wraps from the last feed to the first");
Check((await navigator.SwitchAsync(-1))?.Channel.Id == 1, "Channel- wraps from the first feed to the last");
Check((await navigator.SwitchAsync(-1))?.Channel.Id == 2, "Channel- opens the previous feed");
handler.ExpireOnce = true;
await client.ChannelsAsync();
Check(handler.Logins == 2, "expired session reauthenticates and retries");
await client.KeepAliveAsync();
Check(handler.Keepalives == 1, "keepalive stays on NextPVR server");
await client.LogoutAsync();
Check(!handler.Authenticated, "session logout");
foreach (var bad in new[] { "file:///tmp/x", "ftp://server", "http://user:pin@server", "http://server/?pin=1234" })
{
    try { using var invalid = new NextPvrClient(bad, ""); throw new Exception("invalid URL accepted"); }
    catch (ArgumentException) { passed++; }
}
using var denied = new NextPvrClient("http://nextpvr.test:8866", "9999", new ProtocolHandler());
try { await denied.ConnectAsync(); throw new Exception("invalid PIN accepted"); }
catch (NextPvrException ex) { Check(ex.Code == 1 && !ex.Message.Contains("9999"), "invalid PIN rejected without disclosure"); }
Console.WriteLine($"{passed} checks passed. No HTTP listener or background service was started.");

sealed class ProtocolHandler : HttpMessageHandler
{
    public int Logins, Keepalives;
    public bool Authenticated, ExpireOnce;
    public string? LastStart, ScheduledId;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (uri.Host != "nextpvr.test" || uri.AbsolutePath != "/service") throw new Exception("Unexpected server");
        var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
        var method = query["method"];
        string xml;
        if (method == "session.initiate") xml = "<sid>session &amp; token</sid><salt>test-salt</salt>";
        else if (method == "session.login")
        {
            if (query["sid"] != "session & token" || query["md5"] != NextPvrClient.Hash(":" + "4a7d1ed414474e4033ac29ccb8653d9b" + ":test-salt")) return Reply("<rsp stat='fail'><err code='1'/></rsp>");
            Logins++; Authenticated = true; xml = "";
        }
        else
        {
            if (!Authenticated || query["sid"] != "session & token") throw new Exception("Missing SID");
            if (ExpireOnce) { ExpireOnce = false; Authenticated = false; return Reply("<rsp stat='fail'><err code='8'/></rsp>"); }
            switch (method)
            {
                case "channel.list": xml = "<channels><channel><id>1</id><name>TV &amp; Notícias</name><number>10</number><minor>2</minor></channel><channel><id>2</id><name>Radio</name><number>2</number><type>0xa</type></channel></channels>"; break;
                case "channel.listings": LastStart = query["start"]; xml = "<listings><l><id>99</id><name>Programa</name><description>Descrição</description><start>1800000000000</start><end>1800003600</end></l></listings>"; break;
                case "recording.save": ScheduledId = query["event_id"]; if (query["recording_id"] != "0") throw new Exception("Incorrect recording ID"); xml = ""; break;
                case "recording.list": xml = "<recordings><recording><id>12</id><name>Filme</name><status>Ready</status><start_time_ticks>1800000000</start_time_ticks><duration_seconds>3600</duration_seconds></recording><recording><id>13</id><name>Outro</name><status>Pending</status><start_time_ticks>1700000000</start_time_ticks></recording></recordings>"; break;
                case "session.valid": Keepalives++; xml = ""; break;
                case "session.logout": Authenticated = false; xml = ""; break;
                default: throw new Exception("Unexpected method " + method);
            }
        }
        return Reply("<rsp stat='ok'>" + xml + "</rsp>");
    }
    private static Task<HttpResponseMessage> Reply(string xml) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") });
}
