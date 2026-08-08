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
    private readonly IGraphicPublisher _graphicPublisher;
    private readonly IPublicationPipeline _publicationPipeline;
    private readonly IExternalPublicationIdentityResolver _identityResolver;

    public SynchronizationEngine(
        ILogger<SynchronizationEngine> logger,
        IInputPackageProvider packageProvider,
        IEditionRepository editionRepository,
        IEditionFactory editionFactory,
        ISituationModel situationModel,
        IReasoningModel reasoningModel,
        IEditorialDecisionEngine decisionEngine,
        IEditionAssembly editionAssembly,
        IGraphicPublisher graphicPublisher,
        IPublicationPipeline publicationPipeline,
        IExternalPublicationIdentityResolver identityResolver)
    {
        _logger = logger;
        _packageProvider = packageProvider;
        _editionRepository = editionRepository;
        _editionFactory = editionFactory;
        _situationModel = situationModel;
        _reasoningModel = reasoningModel;
        _decisionEngine = decisionEngine;
        _editionAssembly = editionAssembly;
        _graphicPublisher = graphicPublisher;
        _publicationPipeline = publicationPipeline;
        _identityResolver = identityResolver;
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
                    
                    bool isTomorrow = situation.Type == Situation.TomorrowForecastAppeared || 
                                      situation.Type == Situation.TomorrowForecastDisappeared;
                    
                    string payloadText = string.Empty;
                    if (package?.Payloads != null && situation.TerritoryId != null)
                    {
                        var portion = isTomorrow ? SourcePortion.Tomorrow : SourcePortion.Today;
                        payloadText = System.Linq.Enumerable.FirstOrDefault(package.Payloads, p => p.TerritoryId == situation.TerritoryId && p.Portion == portion)?.RawText ?? string.Empty;
                    }
                    
                    PublicationType pubType = isTomorrow ? PublicationType.Tomorrow : PublicationType.Text;
                    Classification classification = isTomorrow ? Classification.Ephemeral : Classification.Persistent;

                    if (decision.DecisionResult == DecisionResult.CREATE)
                    {
                        if (situation.Type == Situation.MorningStartup && edition.State == SvitloSk.Publisher.Domain.EditionState.Created)
                        {
                            edition.Activate();
                        }
                        else if ((situation.Type == Situation.TerritoryAppeared || situation.Type == Situation.TomorrowForecastAppeared) && situation.TerritoryId != null)
                        {
                            var pub = new Publication(Guid.NewGuid(), situation.TerritoryId, (PublicationClassification)classification, pubType, DateTimeOffset.UtcNow, payloadText);
                            newPublications.Add(pub);
                            artifactsToBuild.Add(pub);
                        }
                    }
                    else if (decision.DecisionResult == DecisionResult.UPDATE && (situation.Type == Situation.ChangedAddresses || situation.Type == Situation.TomorrowForecastAppeared) && situation.TerritoryId != null)
                    {
                        var existing = System.Linq.Enumerable.FirstOrDefault(pkg.Publications, p => p.TerritoryId == situation.TerritoryId && p.Type == pubType);
                        if (existing != null)
                        {
                            removedPublications.Add(existing);
                            var updatedPub = existing with { ContentHash = payloadText };
                            newPublications.Add(updatedPub);
                            artifactsToBuild.Add(updatedPub);
                        }
                    }
                    else if (decision.DecisionResult == DecisionResult.DELETE && situation.TerritoryId != null)
                    {
                        var existing = System.Linq.Enumerable.FirstOrDefault(pkg.Publications, p => p.TerritoryId == situation.TerritoryId && p.Type == pubType);
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
                
                foreach (var p in removedPublications)
                {
                    if (!newPublications.Any(n => n.Id == p.Id))
                    {
                        try
                        {
                            await _publicationPipeline.DispatchAsync(new PublicationRequest(p.Id.ToString(), ""), cancellationToken);
                        }
                        catch (NotSupportedException ex)
                        {
                            _logger.LogWarning(ex, "Failed to dispatch delete for {PublicationId}: operation not supported", p.Id);
                        }
                    }
                }
                
                if (artifactsToBuild.Any())
                {
                    var builtArtifacts = artifacts.Where(a => artifactsToBuild.Any(b => b.Id == a.PublicationId)).ToList();
                    var graphicPubs = _graphicPublisher.Publish(edition, builtArtifacts);
                    foreach (var gp in graphicPubs)
                    {
                        try 
                        {
                            var existingId = _identityResolver.ResolveExternalIdentity(gp.PublicationId.ToString());
                            if (existingId != null)
                            {
                                throw new NotSupportedException($"Update operations are not supported by the current publisher architecture. PublicationId: {gp.PublicationId}");
                            }

                            var accepted = await _publicationPipeline.DispatchAsync(new PublicationRequest(gp.PublicationId.ToString(), gp.GraphicContent), cancellationToken);
                            _identityResolver.RecordExternalIdentity(gp.PublicationId.ToString(), accepted.MessageId);
                        }
                        catch (NotSupportedException ex)
                        {
                            _logger.LogWarning(ex, "Failed to dispatch update for {PublicationId}: operation not supported", gp.PublicationId);
                        }
                    }
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
