// Source: SYNCHRONIZATION_ENGINE_SPECIFICATION.md
// Section: 4

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;
using SvitloSk.Publisher.Domain.Factories;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Channels;

namespace SvitloSk.Publisher.Runtime;

public class SynchronizationEngine : ISynchronizationEngine
{
    private readonly ILogger<SynchronizationEngine> _logger;
    private readonly IInputPackageProvider _packageProvider;
    private readonly IEditionRepository _editionRepository;
    private readonly IEditionFactory _editionFactory;
    private readonly ISituationModel _situationModel;
    private readonly IReasoningModel _reasoningModel;
    private readonly IEditorialDecisionEngine _decisionEngine;
    private readonly IEditionAssembly _editionAssembly;
    private readonly IPublicationPipeline _publicationPipeline;

    public SynchronizationEngine(
        ILogger<SynchronizationEngine> logger,
        IInputPackageProvider packageProvider,
        IEditionRepository editionRepository,
        IEditionFactory editionFactory,
        ISituationModel situationModel,
        IReasoningModel reasoningModel,
        IEditorialDecisionEngine decisionEngine,
        IEditionAssembly editionAssembly,
        IPublicationPipeline publicationPipeline)
    {
        _logger = logger;
        _packageProvider = packageProvider;
        _editionRepository = editionRepository;
        _editionFactory = editionFactory;
        _situationModel = situationModel;
        _reasoningModel = reasoningModel;
        _decisionEngine = decisionEngine;
        _editionAssembly = editionAssembly;
        _publicationPipeline = publicationPipeline;
    }

    public async Task MaintainPublisherStateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Synchronization cycle started");

        try
        {
            var package = await _packageProvider.GetLatestAsync(cancellationToken);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            
            bool hasChanges = false;
            var edition = _editionRepository.GetByDate(today);
            if (edition == null)
            {
                edition = _editionFactory.Create(today);
                hasChanges = true;
            }

            var infraState = new InfrastructureState(false, false, false);
            var currentTime = DateTimeOffset.UtcNow;
            var cleanupThreshold = TimeSpan.FromHours(20);
            var closeThreshold = TimeSpan.FromHours(24);

            var situations = _situationModel.Detect(edition, package, currentTime, cleanupThreshold, closeThreshold, infraState);

            foreach (var situation in situations)
            {
                var conclusion = _reasoningModel.Evaluate(situation, edition);
                var decision = _decisionEngine.Process(conclusion);

                if (decision.DecisionResult != DecisionResult.NO_ACTION)
                {
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                _editionRepository.Save(edition);
                
                var editionArtifact = _editionAssembly.Assemble(edition, Array.Empty<PublicationArtifact>());
                var content = string.Join("\n", editionArtifact.OrderedContent);
                _publicationPipeline.Dispatch(new PublicationRequest(Guid.NewGuid().ToString(), content));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Synchronization failed");
            throw;
        }

        _logger.LogInformation("Synchronization cycle finished");
    }
}
