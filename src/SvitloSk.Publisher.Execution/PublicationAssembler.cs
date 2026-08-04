using System.Text;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Infrastructure;

namespace SvitloSk.Publisher.Execution;

public class PublicationAssembler
{
    public Publication? Assemble(EditorialDecision decision)
    {
        if (decision.Type != DecisionType.CREATE)
            return null; // Walking skeleton simplification
            
        var district = decision.Payload as TextDistrict;
        if (district == null || district.OutageEvents.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine($"[{district.DistrictName}]");
        
        foreach (var evt in district.OutageEvents)
        {
            sb.AppendLine($"{evt.Locality} | {evt.TimeWindow}");
            foreach (var street in evt.StreetLines)
            {
                sb.AppendLine($"{street}");
            }
            sb.AppendLine();
        }

        return new Publication(district.DistrictName, sb.ToString().TrimEnd());
    }
}
