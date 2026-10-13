using System.Globalization;
using System.Text;

namespace NextPvr.Core;

public static class Presentation
{
    public const string Background = "#101822";
    public const string Surface = "#243344";
    public const string Accent = "#16B8D4";
    public const int ControlsTimeoutMs = 4000;
    public static bool Matches(Channel channel, string? query)
    {
        var words = Normalize(query ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var text = Normalize(channel.Number + " " + channel.Name);
        return words.All(word => text.Contains(word));
    }
    private static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) result.Append(char.ToUpperInvariant(ch));
        return result.ToString();
    }
}
