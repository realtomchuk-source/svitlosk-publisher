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
using SvitloSk.Publisher.Runtime.Persistence;

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
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;

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
        IExternalPublicationIdentityResolver identityResolver,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork)
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
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
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
            var cleanupThreshold = TimeSpan.FromHours(23);
            var closeThreshold = TimeSpan.FromHours(24);
            var tomorrowEligibilityThreshold = TimeSpan.FromHours(12);

            var situations = _situationModel.Detect(edition, package, currentTime, cleanupThreshold, closeThreshold, infraState, tomorrowEligibilityThreshold);

            var affectedPublications = new HashSet<Publication>();
            var newPublications = new List<Publication>();
            var removedPublications = new List<Publication>();
            var publicationsToPhysicalDelete = new List<Publication>();
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
                    
                    string contentHash = string.Empty;
                    string? rawMarkdown = null;
                    if (package?.Events != null && situation.TerritoryId != null)
                    {
                        var groupEvents = package.Events.Where(e => e.Settlement == situation.TerritoryId).ToList();
                        
                        if (package.TerritoryPayloads != null && package.TerritoryPayloads.TryGetValue(situation.TerritoryId, out var payload))
                        {
                            rawMarkdown = payload;
                        }
                        
                        var kyivTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
                        var currentKyiv = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, kyivTz);
                        var todayStart = currentKyiv.Date;
                        var todayWindowStart = new DateTimeOffset(todayStart, kyivTz.GetUtcOffset(todayStart));
                        var tomorrowWindowStart = new DateTimeOffset(todayStart.AddDays(1), kyivTz.GetUtcOffset(todayStart.AddDays(1)));
                        var dayAfterWindowStart = new DateTimeOffset(todayStart.AddDays(2), kyivTz.GetUtcOffset(todayStart.AddDays(2)));

                        if (isTomorrow)
                        {
                            contentHash = EventHashGenerator.GenerateHash(groupEvents, tomorrowWindowStart, dayAfterWindowStart, rawMarkdown, package.QueueSchedules);
                        }
                        else
                        {
                            contentHash = EventHashGenerator.GenerateHash(groupEvents, todayWindowStart, tomorrowWindowStart, rawMarkdown, package.QueueSchedules);
                        }
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
                            var pub = new Publication(Guid.NewGuid(), situation.TerritoryId, (PublicationClassification)classification, pubType, DateTimeOffset.UtcNow, contentHash, null, null, null, rawMarkdown, package?.QueueSchedules);
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
                            var updatedPub = existing with { ContentHash = contentHash, PayloadText = rawMarkdown, GraphicData = package?.QueueSchedules };
                            newPublications.Add(updatedPub);
                            artifactsToBuild.Add(updatedPub);
                        }
                    }
                    else if ((decision.DecisionResult == DecisionResult.DELETE || decision.DecisionResult == DecisionResult.REMOVE_EPHEMERAL) && situation.TerritoryId != null)
                    {
                        var existing = System.Linq.Enumerable.FirstOrDefault(pkg.Publications, p => p.TerritoryId == situation.TerritoryId && p.Type == pubType);
                        if (existing != null)
                        {
                            removedPublications.Add(existing);
                            if (decision.DecisionResult == DecisionResult.DELETE)
                            {
                                publicationsToPhysicalDelete.Add(existing);
                            }
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
                    artifacts.Add(new PublicationArtifact(pub.Id, pub.TerritoryId, pub.Classification, pub.PayloadText ?? string.Empty, pub.GraphicData));
                }
                
                foreach (var p in publicationsToPhysicalDelete)
                {
                    if (!newPublications.Any(n => n.Id == p.Id))
                    {
                        var existingId = _identityResolver.ResolveExternalIdentity(p.Id.ToString());
                        if (existingId != null)
                        {
                            var outboxMsg = new SvitloSk.Publisher.Runtime.Persistence.OutboxMessage
                            {
                                OperationId = Guid.NewGuid(),
                                PublicationId = p.Id.ToString(),
                                OperationType = TransportOperation.DELETE,
                                ArtifactType = TransportArtifactType.TEXT_ONLY,
                                ExternalIdentity = existingId,
                                Payload = null,
                                Status = SvitloSk.Publisher.Runtime.Persistence.OutboxOperationStatus.Pending,
                                CreatedAt = DateTimeOffset.UtcNow
                            };
                            _outboxRepository.Add(outboxMsg);
                        }
                    }
                }
                
                if (artifactsToBuild.Any())
                {
                    var builtArtifacts = artifacts.Where(a => artifactsToBuild.Any(b => b.Id == a.PublicationId)).ToList();
                    var graphicPubs = _graphicPublisher.Publish(edition, builtArtifacts);
                    foreach (var gp in graphicPubs)
                    {
                        var existingId = _identityResolver.ResolveExternalIdentity(gp.PublicationId.ToString());
                        var opType = existingId != null ? TransportOperation.UPDATE : TransportOperation.CREATE;
                        
                        var correspondingTextArtifact = builtArtifacts.FirstOrDefault(a => a.PublicationId == gp.PublicationId);
                        var singleMediaPayload = new SingleMediaPayload(gp.GraphicContent, correspondingTextArtifact?.Content ?? string.Empty);
                        var payloadJson = System.Text.Json.JsonSerializer.Serialize(singleMediaPayload);

                        var outboxMsg = new SvitloSk.Publisher.Runtime.Persistence.OutboxMessage
                        {
                            OperationId = Guid.NewGuid(),
                            PublicationId = gp.PublicationId.ToString(),
                            OperationType = opType,
                            ArtifactType = TransportArtifactType.SINGLE_MEDIA,
                            ExternalIdentity = existingId,
                            Payload = payloadJson,
                            Status = SvitloSk.Publisher.Runtime.Persistence.OutboxOperationStatus.Pending,
                            CreatedAt = DateTimeOffset.UtcNow
                        };
                        _outboxRepository.Add(outboxMsg);
                    }
                }
                
                await _unitOfWork.CommitAsync(cancellationToken);
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
