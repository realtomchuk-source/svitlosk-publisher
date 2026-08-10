using System;

namespace SvitloSk.Publisher.Runtime;

public class InputSourcesOptions
{
    public JsonSourceOptions Json { get; set; } = new();
    public TextSourceOptions Text { get; set; } = new();
}

public class JsonSourceOptions
{
    public string BaseUrl { get; set; } = "https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts";
}

public class TextSourceOptions
{
    public string BaseUrl { get; set; } = "https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts";
}
