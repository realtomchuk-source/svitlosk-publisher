using System.Collections.Generic;
using System.Linq;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public class CanonicalOrderingStrategy : IEditorialOrderingStrategy
{
    public IEnumerable<string> Order(Edition edition, IEnumerable<PublicationArtifact> publications)
    {
        var editorialOrder = new EditorialOrder(edition);
        var pubDict = publications.ToDictionary(p => p.PublicationId);
        
        var ordered = new List<string>();
        foreach (var id in editorialOrder.OrderedPublicationIds)
        {
            if (pubDict.TryGetValue(id, out var art))
            {
                ordered.Add(art.Content);
            }
        }
        
        return ordered;
    }
}
