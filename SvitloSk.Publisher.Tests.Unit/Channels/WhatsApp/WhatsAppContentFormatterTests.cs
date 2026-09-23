using System;
using System.Collections.Generic;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Channels.WhatsApp;

public class WhatsAppContentFormatterTests
{
    [Fact]
    public void TC_RenderCityPost_FormatsAdministrativeCenter_WithApprovedStandard()
    {
        var emergencyRecords = new List<OutageRecord>
        {
            new OutageRecord(
                "АВАРІЙНІ",
                "Місто Старокостянтинів",
                "м. Старокостянтинів | з 10:00 до 14:00\nвул. Івана Франка 1, 2, 3, 4, 5\nвул. Попова 4, 6, 8"
            )
        };

        var plannedRecords = new List<OutageRecord>
        {
            new OutageRecord(
                "ПЛАНОВІ",
                "Місто Старокостянтинів",
                "м. Старокостянтинів | з 09:00 до 17:00\nвул. Миру 10, 11, 12, 13, 14"
            )
        };

        var data = new AggregatedTerritoryData(
            "starokostiantyniv",
            "Місто Старокостянтинів",
            emergencyRecords,
            plannedRecords
        );

        string output = WhatsAppContentFormatter.RenderTerritoryPost(data);

        // 1. Must use the exact approved title: *м. СТАРОКОСТЯНТИНІВ*
        Assert.Contains("*м. СТАРОКОСТЯНТИНІВ*", output);

        // 2. Emergency block with quote callout and unified bold time badge
        Assert.Contains("> *АВАРІЙНІ ЗНЕСТРУМЛЕННЯ* • *10:00 – 14:00*", output);
        Assert.Contains("> - вул. Івана Франка: 1–5", output);
        Assert.Contains("> - вул. Попова: 4, 6, 8", output);

        // 3. Planned block with time badge and list
        Assert.Contains("*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ* • *09:00 – 17:00*", output);
        Assert.Contains("- вул. Миру: 10–14", output);

        // 4. Absolute absence of HTML tags
        Assert.DoesNotContain("<b>", output);
        Assert.DoesNotContain("</b>", output);
        Assert.DoesNotContain("<blockquote>", output);
        Assert.DoesNotContain("</blockquote>", output);
        Assert.DoesNotContain("<br>", output);

        // 5. Absolute absence of emojis
        Assert.DoesNotContain("⚡", output);
        Assert.DoesNotContain("📋", output);
        Assert.DoesNotContain("💡", output);
        Assert.DoesNotContain("📍", output);
        Assert.DoesNotContain("🚨", output);
    }

    [Fact]
    public void TC_RenderDistrictPost_FormatsOkruh_WithMultiVillageIntervals()
    {
        var plannedRecords = new List<OutageRecord>
        {
            new OutageRecord(
                "ПЛАНОВІ",
                "Пашковецький старостинський округ",
                "с. Пашківці | з 08:30 до 16:30\nвул. Центральна 12, 13, 14, 15\n\nс. Григорівка | з 09:00 до 17:00\nвул. Шевченка 2, 4, 6"
            )
        };

        var data = new AggregatedTerritoryData(
            "pashkivtsi",
            "Пашковецький старостинський округ",
            Array.Empty<OutageRecord>(),
            plannedRecords
        );

        string output = WhatsAppContentFormatter.RenderTerritoryPost(data);

        Assert.Contains("*ПАШКОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ*", output);
        Assert.Contains("*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ*", output);
        Assert.Contains("*с. Пашківці* • *08:30 – 16:30*", output);
        Assert.Contains("- вул. Центральна: 12–15", output);
        Assert.Contains("*с. Григорівка* • *09:00 – 17:00*", output);
        Assert.Contains("- вул. Шевченка: 2, 4, 6", output);

        // No HTML tags or emojis
        Assert.DoesNotContain("<", output);
        Assert.DoesNotContain(">", output.Replace("> ", "")); // Only WhatsApp quote allowed
        Assert.DoesNotContain("⚡", output);
        Assert.DoesNotContain("📋", output);
    }

    [Fact]
    public void TC_RenderJournalHeader_FormatsFullDateAndSettlements()
    {
        var stats = new JournalSummaryStats(
            TotalTerritories: 2,
            PlannedSettlements: new[] { "м. Старокостянтинів", "с. Пашківці" },
            EmergencySettlements: new[] { "м. Старокостянтинів" }
        );

        string output = WhatsAppContentFormatter.RenderJournalHeader("2026-09-19", stats);

        Assert.Contains("*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", output);
        Assert.Contains("19 вересня 2026 року, субота", output);
        Assert.Contains("*Планові знеструмлення:* м. Старокостянтинів, с. Пашківці", output);
        Assert.Contains("*Аварійні знеструмлення:* м. Старокостянтинів", output);
        Assert.Contains("_Інформація оновлюється автоматично протягом доби_", output);

        Assert.DoesNotContain("<b>", output);
        Assert.DoesNotContain("⚡", output);
    }

    [Fact]
    public void TC_RenderNoOutagesPost_OutputsCleanAbsenceState()
    {
        string output = WhatsAppContentFormatter.RenderNoOutagesPost("2026-09-19");

        Assert.Contains("*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", output);
        Assert.Contains("*Планові знеструмлення:* відсутні", output);
        Assert.Contains("*Аварійні знеструмлення:* відсутні", output);
        Assert.Contains("_Інформація оновлюється автоматично протягом доби_", output);
    }

    [Fact]
    public void TC_RenderTomorrowPost_IncludesTomorrowForecastHeader()
    {
        var plannedRecords = new List<OutageRecord>
        {
            new OutageRecord(
                "ПЛАНОВІ",
                "Пашковецький старостинський округ",
                "с. Пашківці | з 09:00 до 17:00\nвул. Центральна 1, 2, 3"
            )
        };

        var data = new AggregatedTerritoryData(
            "pashkivtsi",
            "Пашковецький старостинський округ",
            Array.Empty<OutageRecord>(),
            plannedRecords
        );

        string output = WhatsAppContentFormatter.RenderTerritoryPost(data, isTomorrow: true, tomorrowDate: "2026-09-20");

        Assert.Contains("*ПРОГНОЗ НА ЗАВТРА* • *20.09.2026*", output);
        Assert.Contains("*ПАШКОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ*", output);
        Assert.Contains("*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ* • *09:00 – 17:00*", output);
        Assert.Contains("- вул. Центральна: 1–3", output);
    }

    [Fact]
    public void TC_RenderSystemStatus_OutputsUnifiedBoldTime()
    {
        var testUtc = new DateTime(2026, 9, 19, 11, 15, 0, DateTimeKind.Utc);
        string output = WhatsAppContentFormatter.RenderSystemStatus(testUtc, "активний");

        // 11:15 UTC + 3 hours = 14:15
        Assert.Contains("Останнє оновлення журналу: *14:15*", output);
        Assert.Contains("Стан моніторингу: активний", output);
    }

    [Fact]
    public void TC_SplitMessage_SplitsLongTexts_AtLineBoundaries()
    {
        var longTextBuilder = new System.Text.StringBuilder();
        for (int i = 1; i <= 200; i++)
        {
            longTextBuilder.AppendLine($"- вул. Тестова {i}: 1–50");
        }
        string fullText = longTextBuilder.ToString();

        var chunks = WhatsAppContentFormatter.SplitMessage(fullText, limit: 1000);

        Assert.True(chunks.Count > 1);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= 1000);
            Assert.DoesNotContain("\n\n\n", chunk);
        }
    }

    [Fact]
    public void TC_ConvertToWhatsAppMarkdown_AvoidsNestedAsterisksInTimeHeaders()
    {
        string input = "<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ (10:00 – 17:00)</b>\n\n<b>с. Великі Мацевичі</b>\n- вул. Затишна: 2, 4–8";
        string output = WhatsAppContentFormatter.ConvertToWhatsAppMarkdown(input);

        Assert.Contains("*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ* • *10:00 – 17:00*", output);
        Assert.DoesNotContain("*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ • *", output);
        Assert.DoesNotContain("**", output);
    }
}

