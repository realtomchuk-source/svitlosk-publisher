// Source: SYNCHRONIZATION_ENGINE_SPECIFICATION.md
// Section: 4

using System;
using System.Linq;
using System.Collections.Generic;
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

            var affectedPublications = new HashSet<Publication>();
            var newPublications = new List<Publication>();
            var removedPublications = new List<Publication>();
            var artifactsToBuild = new List<Publication>();

            var pkg = System.Linq.Enumerable.FirstOrDefault(edition.Packages, p => p.Name == "Default Package");
            if (pkg == null)
            {
                pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
                edition.AddPackage(pkg);
            }

            foreach (var situation in situations)
            {
                var conclusion = _reasoningModel.Evaluate(situation, edition);
                var decision = _decisionEngine.Process(conclusion);

                if (decision.DecisionResult != DecisionResult.NO_ACTION)
                {
                    hasChanges = true;
                    if (decision.DecisionResult == DecisionResult.CREATE)
                    {
                        if (situation.Type == Situation.MorningStartup && edition.State == SvitloSk.Publisher.Domain.EditionState.Created)
                        {
                            edition.Activate();
                            if (package != null && !string.IsNullOrEmpty(package.TerritorialScope))
                            {
                                var pub = new Publication(Guid.NewGuid(), package.TerritorialScope, PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, package.RawPayload ?? string.Empty);
                                newPublications.Add(pub);
                                artifactsToBuild.Add(pub);
                            }
                        }
                        else if (situation.Type == Situation.TerritoryAppeared && situation.TerritoryId != null)
                        {
                            var pub = new Publication(Guid.NewGuid(), situation.TerritoryId, PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, package?.RawPayload ?? string.Empty);
                            newPublications.Add(pub);
                            artifactsToBuild.Add(pub);
                        }
                    }
                    else if (decision.DecisionResult == DecisionResult.UPDATE && situation.Type == Situation.ChangedAddresses && situation.TerritoryId != null)
                    {
                        var existing = System.Linq.Enumerable.FirstOrDefault(pkg.Publications, p => p.TerritoryId == situation.TerritoryId);
                        if (existing != null)
                        {
                            removedPublications.Add(existing);
                            var updatedPub = existing with { ContentHash = package?.RawPayload ?? string.Empty };
                            newPublications.Add(updatedPub);
                            artifactsToBuild.Add(updatedPub);
                        }
                    }
                    else if (decision.DecisionResult == DecisionResult.DELETE && situation.TerritoryId != null)
                    {
                        var existing = System.Linq.Enumerable.FirstOrDefault(pkg.Publications, p => p.TerritoryId == situation.TerritoryId);
                        if (existing != null)
                        {
                            removedPublications.Add(existing);
                        }
                    }
                    else if (decision.DecisionResult == DecisionResult.CLOSE && edition.State != SvitloSk.Publisher.Domain.EditionState.Closed)
                    {
                        edition.Close();
                    }
                }
            }

            foreach (var p in removedPublications) pkg.RemovePublication(p);
            foreach (var p in newPublications) pkg.AddPublication(p);

            if (hasChanges)
            {
                _editionRepository.Save(edition);
                
                var artifacts = new List<PublicationArtifact>();
                foreach (var pub in pkg.Publications)
                {
                    artifacts.Add(new PublicationArtifact(pub.Id, pub.TerritoryId, pub.Classification, $"Content for {pub.TerritoryId} {pub.ContentHash}"));
                }
                
                if (pkg.Publications.Any())
                {
                    var editionArtifact = _editionAssembly.Assemble(edition, artifacts);
                    var content = string.Join("\n", editionArtifact.OrderedPublications.Select(p => p.Content));
                    _publicationPipeline.Dispatch(new PublicationRequest(Guid.NewGuid().ToString(), content));
                }
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
