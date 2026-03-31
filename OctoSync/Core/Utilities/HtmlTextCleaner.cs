using System.Text.RegularExpressions;

namespace OctoSync.Core.Utilities;

public static partial class HtmlTextCleaner
{
    public static string StripHtmlTags(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var stripped = HtmlTagRegex().Replace(html, string.Empty);
        stripped = WhitespaceRegex().Replace(stripped, " ");

        return System.Net.WebUtility.HtmlDecode(stripped).Trim();
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}