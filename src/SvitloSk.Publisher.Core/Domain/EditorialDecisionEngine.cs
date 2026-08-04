using System.Collections.Generic;
using SvitloSk.Publisher.Core.Infrastructure;

namespace SvitloSk.Publisher.Core.Domain;

public class EditorialDecisionEngine
{
    public List<EditorialDecision> Evaluate(TextPackage package)
    {
        var decisions = new List<EditorialDecision>();
        
        foreach (var district in package.PlannedDistricts)
        {
            // Implementation decision: Direct mapping to CREATE for Walking Skeleton
            decisions.Add(new EditorialDecision(DecisionType.CREATE, district.DistrictName, district));
        }
        
        return decisions;
    }
}
