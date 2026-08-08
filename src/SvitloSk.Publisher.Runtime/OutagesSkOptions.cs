using System;

namespace SvitloSk.Publisher.Runtime;

public class OutagesSkOptions
{
    public string TodayUrl { get; set; } = "https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/today.txt";
    public string TomorrowUrl { get; set; } = "https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/tomorrow.txt";
}
