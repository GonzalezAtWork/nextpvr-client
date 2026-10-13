namespace NextPvr.Core;

public sealed record Channel(int Id, string Name, string Number, bool Radio)
{
    public override string ToString() => $"{Number}  •  {Name}";
}
public sealed record Programme(int Id, string Name, string Description, DateTimeOffset Start, DateTimeOffset End)
{
    public override string ToString() => $"{Start.LocalDateTime:HH:mm} – {End.LocalDateTime:HH:mm}  {Name}";
}
public sealed record Recording(int Id, string Name, string Description, string Status, DateTimeOffset Start, int Duration)
{
    public bool Playable => Status is "Ready" or "Recording";
    public override string ToString() => $"{Name}  •  {Start.LocalDateTime:dd/MM HH:mm}  •  {Status}";
}
public sealed class NextPvrException(string message, int code = 0) : Exception(message)
{
    public int Code { get; } = code;
}
