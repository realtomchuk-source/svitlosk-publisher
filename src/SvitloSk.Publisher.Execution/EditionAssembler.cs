using System.Collections.Generic;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Infrastructure;

namespace SvitloSk.Publisher.Execution;

public class EditionAssembler
{
    private readonly PublicationAssembler _pubAssembler;

    public EditionAssembler(PublicationAssembler pubAssembler)
    {
        _pubAssembler = pubAssembler;
    }

    public Edition Assemble(TextPackage metadata, List<EditorialDecision> decisions)
    {
        var publications = new List<Publication>();
        
        foreach (var decision in decisions)
        {
            var pub = _pubAssembler.Assemble(decision);
            if (pub != null)
            {
                publications.Add(pub);
            }
        }
        
        return new Edition(metadata.Date, metadata.EmergencyText, publications);
    }
}
