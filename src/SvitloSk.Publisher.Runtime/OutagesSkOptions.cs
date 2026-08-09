using System;

namespace SvitloSk.Publisher.Runtime;

public class OutagesSkOptions
{
    public string SnapshotUrl { get; set; } = "https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/outages_snapshot.json";
}
