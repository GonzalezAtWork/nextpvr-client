namespace NextPvr.Core;

public sealed record LiveFeed(Channel Channel, Uri Uri);

/// <summary>Cycles through the complete server list, independently of the channel search.</summary>
public sealed class LiveChannelNavigator(NextPvrClient client, int? channelId, string channelName)
{
    private readonly SemaphoreSlim switching = new(1, 1);
    private IReadOnlyList<Channel>? channels;
    private int? currentId = channelId;

    public async Task<LiveFeed?> SwitchAsync(int direction, CancellationToken ct = default)
    {
        if (!await switching.WaitAsync(0, ct)) return null;
        try
        {
            channels ??= await client.ChannelsAsync(ct);
            if (channels.Count == 0) throw new ArgumentException("Nenhum canal disponível no servidor.");
            int index = -1;
            for (int i = 0; i < channels.Count; i++)
                if (currentId.HasValue ? channels[i].Id == currentId.Value : channels[i].Name == channelName) { index = i; break; }
            int next = index < 0 ? 0 : (index + Math.Sign(direction) + channels.Count) % channels.Count;
            var channel = channels[next];
            var uri = await client.LiveUriAsync(channel.Id, ct);
            currentId = channel.Id;
            return new LiveFeed(channel, uri);
        }
        finally { switching.Release(); }
    }
}
