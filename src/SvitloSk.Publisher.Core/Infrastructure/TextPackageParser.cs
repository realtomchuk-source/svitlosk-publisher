using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Infrastructure;

public record TextPackage(
    string Date,
    string EmergencyText,
    List<TextDistrict> PlannedDistricts
);

public record TextDistrict(
    string DistrictName,
    List<TextOutageEvent> OutageEvents
);

public record TextOutageEvent(
    string Locality,
    string TimeWindow,
    List<string> StreetLines
);

public class TextPackageParser
{
    public TextPackage Parse(string content)
    {
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        string date = "";
        string emergencyText = "Аварійних знеструмлень не зафіксовано.";
        var plannedDistricts = new List<TextDistrict>();
        
        TextDistrict? currentDistrict = null;
        TextOutageEvent? currentEvent = null;
        
        int state = 0; // 0=header, 1=emergency, 2=planned
        
        foreach (var line in lines)
        {
            if (line.StartsWith("Дата:"))
            {
                date = line.Substring(5).Trim();
            }
            else if (line.Contains("--- АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ---"))
            {
                state = 1;
            }
            else if (line.Contains("--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---"))
            {
                state = 2;
            }
            else if (state == 1 && !line.StartsWith("===") && !line.StartsWith("КІНЕЦЬ") && !string.IsNullOrWhiteSpace(line))
            {
                if (emergencyText == "Аварійних знеструмлень не зафіксовано.") 
                    emergencyText = line;
                else
                    emergencyText += "\n" + line;
            }
            else if (state == 2)
            {
                if (line.StartsWith("===") || line.StartsWith("КІНЕЦЬ") || line.StartsWith("Кількість"))
                {
                    continue;
                }
                else if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentDistrict = new TextDistrict(line.Trim('[', ']'), new List<TextOutageEvent>());
                    plannedDistricts.Add(currentDistrict);
                    currentEvent = null;
                }
                else if (line.Contains("|"))
                {
                    var parts = line.Split('|', 2);
                    currentEvent = new TextOutageEvent(parts[0].Trim(), parts[1].Trim(), new List<string>());
                    if (currentDistrict == null) 
                    {
                        currentDistrict = new TextDistrict("Невідомий округ", new List<TextOutageEvent>());
                        plannedDistricts.Add(currentDistrict);
                    }
                    currentDistrict.OutageEvents.Add(currentEvent);
                }
                else if (line.StartsWith("-") && currentEvent != null)
                {
                    currentEvent.StreetLines.Add(line.Trim());
                }
            }
        }
        
        return new TextPackage(date, emergencyText, plannedDistricts);
    }
}
