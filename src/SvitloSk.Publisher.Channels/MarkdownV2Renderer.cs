using System.Text.RegularExpressions;

namespace SvitloSk.Publisher.Channels;

public class MarkdownV2Renderer
{
    public string Escape(string text)
    {
        var pattern = @"([_\*\[\]\(\)~`\>#\+\-\=\|\{\}\.\!\\])";
        return Regex.Replace(text, pattern, "\\$1");
    }
}
