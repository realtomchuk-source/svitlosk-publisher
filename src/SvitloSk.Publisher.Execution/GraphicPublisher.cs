// Source: GRAPHIC_PUBLISHER_SPECIFICATION.md
// Section: 1

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public class GraphicPublisher : IGraphicPublisher
{
    private readonly ILogger<GraphicPublisher> _logger;
    private readonly IEditionAssembly _editionAssembly;

    public GraphicPublisher(ILogger<GraphicPublisher> logger, IEditionAssembly editionAssembly)
    {
        _logger = logger;
        _editionAssembly = editionAssembly;
    }

    public IReadOnlyCollection<GraphicPublication> Publish(Edition edition, IReadOnlyCollection<PublicationArtifact> artifacts)
    {
        _logger.LogInformation("Generating graphic publications for Edition {EditionDate}", edition.TargetDate);
        
        var editionArtifact = _editionAssembly.Assemble(edition, artifacts);
        var result = new List<GraphicPublication>();
        
        foreach (var artifact in editionArtifact.OrderedPublications)
        {
            result.Add(new GraphicPublication(
                artifact.PublicationId,
                artifact.Territory,
                artifact.Classification,
                $"GRAPHIC_[{artifact.Content}]"
            ));
        }

        return result;
    }
}
