using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Runtime;

public class RealOutagesSkInputPackageProvider : IInputPackageProvider
{
    private readonly HttpClient _httpClient;
    private readonly OutagesSkOptions _options;
    private readonly ILogger<RealOutagesSkInputPackageProvider> _logger;

    public RealOutagesSkInputPackageProvider(
        HttpClient httpClient,
        IOptions<OutagesSkOptions> options,
        ILogger<RealOutagesSkInputPackageProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken)
    {
        var payloads = new List<TerritorialPayload>();
        
        try
        {
            var todayTask = _httpClient.GetStringAsync(_options.TodayUrl, cancellationToken);
            var tomorrowTask = _httpClient.GetStringAsync(_options.TomorrowUrl, cancellationToken);

            var todayContent = await todayTask;
            var tomorrowContent = await tomorrowTask;

            ParseContent(todayContent, SourcePortion.Today, payloads);
            ParseContent(tomorrowContent, SourcePortion.Tomorrow, payloads);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch or parse real OutagesSk source.");
            throw; // Re-throw to prevent generating corrupted empty packages
        }

        return new InputPackage(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "RealOutagesSk",
            "Starokostiantyniv Urban Territorial Community",
            payloads
        );
    }

    private void ParseContent(string content, SourcePortion portion, List<TerritorialPayload> payloads)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        // Split by territorial blocks using regex
        // Pattern matches: [TerritoryName] followed by content until the next [ or End of String
        var matches = Regex.Matches(content, @"\[(.*?)\](.*?)(?=\n\[|$)", RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var territoryName = match.Groups[1].Value.Trim();
            var blockContent = match.Groups[2].Value.Trim();
            
            // Remove global footers like "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---" or "============================================"
            // from the last block's content
            if (blockContent.Contains("--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---"))
            {
                var idx = blockContent.IndexOf("--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---");
                blockContent = blockContent.Substring(0, idx).Trim();
            }

            if (!string.IsNullOrEmpty(territoryName) && !string.IsNullOrEmpty(blockContent))
            {
                payloads.Add(new TerritorialPayload(territoryName, portion, blockContent));
            }
        }
    }
}
