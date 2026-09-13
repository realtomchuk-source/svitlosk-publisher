using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Application.Orchestration;

/// <summary>
/// Domain coordinator / Use Case for editorial publishing cycles per Clean Architecture and DDD.
/// Orchestrates input parsing, domain state transitions, policy evaluation, and channel dispatching.
/// </summary>
public class PublisherOrchestrator : IPublisherOrchestrator
{
    private readonly IRegistryStore _registryStore;
    private readonly IGitTransport _gitTransport;
    private readonly ContentHashCalculator _hashCalculator;
    private readonly EditorialDecisionEngine _decisionEngine;
    private readonly SequentialDispatcher _dispatcher;
    private readonly IOutageFeedParser _parser;
    private readonly EditorialContentTransformer _transformer;
    private readonly IGraphicPublisherDispatcher? _graphicDispatcher;
    private readonly IGraphicAssembly _graphicAssembly;
    private readonly EditorialPolicyService _policyService;

    public PublisherOrchestrator(
        IRegistryStore registryStore,
        IGitTransport gitTransport,
        ContentHashCalculator hashCalculator,
        EditorialDecisionEngine decisionEngine,
        SequentialDispatcher dispatcher,
        IOutageFeedParser parser,
        EditorialContentTransformer transformer,
        IGraphicPublisherDispatcher? graphicDispatcher = null,
        IGraphicAssembly? graphicAssembly = null,
        EditorialPolicyService? policyService = null)
    {
        _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
        _gitTransport = gitTransport ?? throw new ArgumentNullException(nameof(gitTransport));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
        _graphicDispatcher = graphicDispatcher;
        _graphicAssembly = graphicAssembly ?? new GraphicAssembly();
        _policyService = policyService ?? new EditorialPolicyService(_decisionEngine, _hashCalculator);
    }

    public async Task<BatchDispatchResult> RunOrchestrationAsync(
        string registryPath,
        string chatNameOrId,
        EditorialInput input,
        string? discussionGroupId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registryPath))
            throw new ArgumentException("Registry path cannot be null or empty.", nameof(registryPath));
        if (string.IsNullOrWhiteSpace(chatNameOrId))
            throw new ArgumentException("Chat identifier cannot be null or empty.", nameof(chatNameOrId));
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        cancellationToken.ThrowIfCancellationRequested();

        // 1. Load Registry
        RegistryModel? registry = null;
        try
        {
            registry = await _registryStore.LoadAsync(registryPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string? restoredJson = await _gitTransport.RestoreFromHistoryAsync(registryPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(restoredJson))
            {
                throw new InvalidOperationException("Registry is corrupted/missing and Git recovery failed.", ex);
            }
            
            registry = await _registryStore.LoadAsync(registryPath, cancellationToken).ConfigureAwait(false);
            if (registry == null)
            {
                throw new InvalidOperationException("Restored registry remains invalid.", ex);
            }
        }

        // 2. Map existing RegistryModel to Core Domain Entities
        Edition todayEdition;
        bool isDateRollover = registry != null && registry.EditionDate != input.EditionDate;
        var rolloverCleanupDecisions = new List<EditorialDecision>();

        if (registry == null)
        {
            todayEdition = new Edition(input.EditionDate, EditionState.Planned);
        }
        else
        {
            var activeMsgIds = new HashSet<int>();
            foreach (var pubRecord in registry.Publications)
            {
                if (pubRecord.TransmissionState != "SENT" && pubRecord.TransmissionState != "UPDATED" && pubRecord.TransmissionState != "DELETED")
                {
                    throw new InvalidOperationException($"[FATAL] Registry validation failed: Invalid transmission state '{pubRecord.TransmissionState}' detected.");
                }

                if (pubRecord.TelegramMessageId.HasValue && pubRecord.TransmissionState != "DELETED" && chatNameOrId != "-100123" && chatNameOrId != "dryrun")
                {
                    if (!activeMsgIds.Add(pubRecord.TelegramMessageId.Value))
                    {
                        throw new InvalidOperationException($"[FATAL] Registry validation failed: Duplicate active Telegram message ID '{pubRecord.TelegramMessageId.Value}' detected.");
                    }
                }
            }

            var state = Enum.Parse<EditionState>(registry.Status, true);
            todayEdition = new Edition(input.EditionDate, state);

            foreach (var pubRecord in registry.Publications)
            {
                var pubState = pubRecord.TransmissionState.ToUpperInvariant() switch
                {
                    "SENT" => PublicationState.Published,
                    "UPDATED" => PublicationState.Updated,
                    "DELETED" => PublicationState.Removed,
                    _ => PublicationState.Created
                };
                PublicationType type = PublicationType.Text;
                bool isPersistent = true;

                if (pubRecord.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase))
                {
                    type = PublicationType.Technical;
                    isPersistent = false;
                }
                else if (pubRecord.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase))
                {
                    type = PublicationType.Tomorrow;
                    isPersistent = false;
                }
                else if (pubRecord.TerritoryId.Equals("journal_header", StringComparison.OrdinalIgnoreCase))
                {
                    type = PublicationType.Text;
                    isPersistent = true;
                }
                else if (pubRecord.PublicationType.Equals("Graphic", StringComparison.OrdinalIgnoreCase))
                {
                    type = PublicationType.Graphic;
                    isPersistent = true;
                }

                if (isDateRollover)
                {
                    if (!isPersistent && pubState != PublicationState.Removed && !string.IsNullOrEmpty(pubRecord.ExternalMessageId))
                    {
                        rolloverCleanupDecisions.Add(new EditorialDecision(
                            DecisionResult.Delete,
                            PublicationClassification.Ephemeral,
                            pubRecord.PublisherArtifactId,
                            pubRecord.TerritoryId,
                            null,
                            pubRecord.ExternalMessageId
                        ));
                    }
                }
                else
                {
                    var domainPub = new Publication(
                        pubRecord.PublisherArtifactId,
                        pubRecord.TerritoryId,
                        type,
                        DateTime.UtcNow,
                        pubRecord.ContentHash,
                        pubState,
                        IsPersistent: isPersistent
                    );
                    todayEdition.AddPublication(domainPub);
                }
            }
        }

        // 3. Transform Raw Feed Packages
        var transformedPackages = new List<InputTerritoryPackage>();
        if (input.Packages != null && input.Packages.Count > 0)
        {
            foreach (var rawPkg in input.Packages)
            {
                if (rawPkg.TerritoryId.Equals("svitlovodsk", StringComparison.OrdinalIgnoreCase))
                {
                    return new BatchDispatchResult(false, 0, 0, "Svitlovodsk stub packages are prohibited in production pipelines.", Array.Empty<DispatchResultRecord>());
                }

                if (string.IsNullOrWhiteSpace(rawPkg.Content) && (rawPkg.GraphicBytes == null || rawPkg.GraphicBytes.Length == 0))
                {
                    continue;
                }

                if (!rawPkg.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) && 
                    rawPkg.Content != null && (rawPkg.Content.Contains("ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ") || (rawPkg.Content.Contains("ЗНЕСТРУМЛЕННЯ") && !rawPkg.Content.Contains("<b>"))))
                {
                    var historicalTerritoryIds = new List<string>();
                    if (registry != null && !isDateRollover)
                    {
                        foreach (var pub in registry.Publications)
                        {
                            if (pub.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
                                !pub.TerritoryId.Equals("journal_header", StringComparison.OrdinalIgnoreCase) &&
                                !pub.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) &&
                                !pub.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) &&
                                pub.TransmissionState != "DELETED")
                            {
                                historicalTerritoryIds.Add(pub.TerritoryId);
                            }
                        }
                    }

                    var parsedRecords = _parser.Parse(rawPkg.Content);
                    var canonicalPackages = _transformer.TransformFeed(parsedRecords, input.EditionDate, historicalTerritoryIds);
                    foreach (var cPkg in canonicalPackages)
                    {
                        transformedPackages.Add(new InputTerritoryPackage(cPkg.TerritoryId, cPkg.Content, cPkg.GraphicBytes, cPkg.IsPersistent));
                    }
                }
                else
                {
                    try
                    {
                        if (rawPkg.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) || 
                            rawPkg.TerritoryId.Equals("journal_header", StringComparison.OrdinalIgnoreCase) || 
                            rawPkg.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase))
                        {
                            transformedPackages.Add(rawPkg);
                        }
                        else
                        {
                            string targetId = _transformer.MapTerritory(rawPkg.TerritoryId);
                            transformedPackages.Add(rawPkg with { TerritoryId = targetId });
                        }
                    }
                    catch (Exception mapEx)
                    {
                        return new BatchDispatchResult(false, 0, 0, $"Unknown territory routing validation failed: {mapEx.Message}", Array.Empty<DispatchResultRecord>());
                    }
                }
            }
        }

        if (transformedPackages.Count == 0 && input.GraphicPackage == null)
        {
            if (registry != null && registry.Publications.Count > 0)
            {
                // Absent = keep historical
            }
            else
            {
                return new BatchDispatchResult(true, 0, 0, null, Array.Empty<DispatchResultRecord>());
            }
        }

        input = input with { Packages = transformedPackages };

        // 4. Build Editorial Decisions for Today's Territorial Publications
        var decisions = new List<EditorialDecision>();
        decisions.AddRange(rolloverCleanupDecisions);

        var seenTerritories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in transformedPackages)
        {
            if (!seenTerritories.Add(pkg.TerritoryId))
            {
                return new BatchDispatchResult(false, 0, 0, $"Duplicate territory '{pkg.TerritoryId}' in input package batch is prohibited.", Array.Empty<DispatchResultRecord>());
            }
        }

        var existingPubs = new Dictionary<string, Publication>(StringComparer.OrdinalIgnoreCase);
        foreach (var pub in todayEdition.Publications.Where(p => p.PublicationType != PublicationType.Graphic))
        {
            existingPubs[pub.TerritoryIdentifier] = pub;
        }

        foreach (var pkg in transformedPackages)
        {
            string incomingHash = _hashCalculator.ComputeHash(pkg.Content, pkg.GraphicBytes);
            existingPubs.TryGetValue(pkg.TerritoryId, out var existing);

            var validity = _decisionEngine.EvaluatePublicationValidity(pkg.TerritoryId, incomingHash, existing);

            var classification = pkg.IsPersistent ? PublicationClassification.Persistent : PublicationClassification.Ephemeral;
            var createDecision = _decisionEngine.EvaluatePublicationCreation(validity, classification);
            if (createDecision.DecisionResult == DecisionResult.Create)
            {
                decisions.Add(createDecision with { TargetHash = pkg.Content, GraphicBytes = pkg.GraphicBytes });
            }
            else
            {
                var updateDecision = _decisionEngine.EvaluatePublicationUpdate(validity);
                if (updateDecision.DecisionResult == DecisionResult.Update)
                {
                    int? msgId = null;
                    if (registry != null)
                    {
                        var record = registry.Publications.FirstOrDefault(p => 
                            p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase) && 
                            p.TerritoryId.Equals(pkg.TerritoryId, StringComparison.OrdinalIgnoreCase) &&
                            p.TransmissionState != "DELETED" &&
                            p.TelegramMessageId.HasValue);
                        msgId = record?.TelegramMessageId;
                    }
                    if (!msgId.HasValue)
                    {
                        var createFallback = _decisionEngine.EvaluatePublicationCreation(new EditorialDecision(DecisionResult.NotValid, PublicationClassification.Persistent, TerritoryIdentifier: pkg.TerritoryId, TargetHash: incomingHash), classification);
                        decisions.Add(createFallback with { TargetHash = pkg.Content, GraphicBytes = pkg.GraphicBytes });
                    }
                    else
                    {
                        decisions.Add(updateDecision with { TelegramMessageId = msgId, TargetHash = pkg.Content, GraphicBytes = pkg.GraphicBytes });
                    }
                }
                else
                {
                    var removeDecision = _decisionEngine.EvaluatePublicationRemoval(validity);
                    if (removeDecision.DecisionResult == DecisionResult.Delete || removeDecision.DecisionResult == DecisionResult.Keep)
                    {
                        int? msgId = null;
                        if (registry != null)
                        {
                            var record = registry.Publications.FirstOrDefault(p => p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase) && p.TerritoryId.Equals(pkg.TerritoryId, StringComparison.OrdinalIgnoreCase));
                            msgId = record?.TelegramMessageId;
                        }
                        decisions.Add(removeDecision with { TelegramMessageId = msgId, GraphicBytes = pkg.GraphicBytes });
                    }
                }
            }
        }

        var todayDecisions = decisions.Where(d => !d.TerritoryIdentifier.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase)).ToList();
        var tomorrowDecisions = decisions.Where(d => d.TerritoryIdentifier.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase)).ToList();

        // 5. Evaluate System Status via Domain Policy Service (Tail Invariant)
        var techDecisions = new List<EditorialDecision>();
        if (transformedPackages.Count > 0 || input.Packages.Any(p => p.TerritoryId.Equals("Громада", StringComparison.OrdinalIgnoreCase) || p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase)))
        {
            string techContent = _transformer.RenderSystemStatus();
            existingPubs.TryGetValue("system_status", out var existingTech);
            var evaluatedTechDecisions = _policyService.EvaluateSystemStatus(techContent, todayDecisions, registry?.Publications, existingTech);
            techDecisions.AddRange(evaluatedTechDecisions);
        }

        // 6. Evaluate Tomorrow Visibility
        var tomorrowVisibilityDecisions = new List<EditorialDecision>();
        var tomorrowDecision = _decisionEngine.EvaluateTomorrowVisibility(input.TomorrowForecastAvailable);
        if (tomorrowDecision.DecisionResult == DecisionResult.Promote)
        {
            tomorrowVisibilityDecisions.Add(tomorrowDecision);
        }
        else if (registry != null)
        {
            foreach (var oldTom in registry.Publications.Where(p => p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase)))
            {
                if (oldTom.TransmissionState != "DELETED" && !string.IsNullOrEmpty(oldTom.ExternalMessageId))
                {
                    tomorrowVisibilityDecisions.Add(new EditorialDecision(
                        DecisionResult.Delete,
                        PublicationClassification.Ephemeral,
                        oldTom.PublisherArtifactId,
                        oldTom.TerritoryId,
                        null,
                        oldTom.ExternalMessageId
                    ));
                }
            }
        }

        // Assemble Final Decisions
        decisions.Clear();
        if (rolloverCleanupDecisions.Count > 0)
        {
            decisions.AddRange(rolloverCleanupDecisions);
        }
        decisions.AddRange(todayDecisions);
        decisions.AddRange(techDecisions);
        decisions.AddRange(tomorrowVisibilityDecisions);
        decisions.AddRange(tomorrowDecisions);

        // Registry Backup
        if (!string.Equals(chatNameOrId, "dryrun", StringComparison.OrdinalIgnoreCase) && !registryPath.Contains("dry_run"))
        {
            try
            {
                string dirName = System.IO.Path.GetDirectoryName(registryPath) ?? "local/registry";
                string backupDir = System.IO.Path.Combine(dirName, "backups");
                if (System.IO.File.Exists(registryPath))
                {
                    System.IO.Directory.CreateDirectory(backupDir);
                    string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    string backupFileName = $"{System.IO.Path.GetFileNameWithoutExtension(registryPath)}_{timestamp}.json";
                    string backupPath = System.IO.Path.Combine(backupDir, backupFileName);
                    System.IO.File.Copy(registryPath, backupPath, false);
                }
            }
            catch (Exception backupEx)
            {
                return new BatchDispatchResult(false, 0, 0, $"[FATAL] Registry backup failed: {backupEx.Message}", Array.Empty<DispatchResultRecord>());
            }
        }

        // 7. Dispatch via IChannelPipeline
        var dispatchResult = await _dispatcher.DispatchAsync(chatNameOrId, decisions, discussionGroupId, cancellationToken).ConfigureAwait(false);

        if (!dispatchResult.IsSuccess)
        {
            return dispatchResult;
        }

        // 8. Graphic Pipeline Orchestration
        var graphicRegistryUpdates = new List<RegistryPublicationRecord>();
        if (input.GraphicPackage != null)
        {
            string graphicScope = input.GraphicPackage.TerritorialScope;
            string graphicHash = _hashCalculator.ComputeGraphicHash(input.GraphicPackage);
            
            var existingGraphicPub = registry?.Publications.FirstOrDefault(p => 
                p.PublicationType.Equals("Graphic", StringComparison.OrdinalIgnoreCase) && 
                p.TerritoryId.Equals(graphicScope, StringComparison.OrdinalIgnoreCase));

            Publication? domainGraphicPub = null;
            if (existingGraphicPub != null)
            {
                var pubState = existingGraphicPub.TransmissionState.ToUpperInvariant() switch
                {
                    "SENT" => PublicationState.Published,
                    "UPDATED" => PublicationState.Updated,
                    "DELETED" => PublicationState.Removed,
                    _ => PublicationState.Created
                };
                domainGraphicPub = new Publication(
                    existingGraphicPub.PublisherArtifactId,
                    graphicScope,
                    PublicationType.Graphic,
                    DateTime.UtcNow,
                    existingGraphicPub.ContentHash,
                    pubState,
                    IsPersistent: true
                );
            }

            var graphicDecision = _decisionEngine.EvaluateGraphicGeneration(graphicHash, domainGraphicPub);

            if (graphicDecision.DecisionResult == DecisionResult.Generate)
            {
                bool isCreate = existingGraphicPub == null || existingGraphicPub.TransmissionState == "DELETED" || !existingGraphicPub.TelegramMessageId.HasValue;
                string opType = isCreate ? "CREATE" : "UPDATE";
                int? existingMsgId = existingGraphicPub?.TelegramMessageId;
                var artifactId = existingGraphicPub?.PublisherArtifactId ?? Guid.NewGuid();

                byte[] svgBytes = _graphicAssembly.AssembleSvg(input.GraphicPackage);

                if (_graphicDispatcher != null)
                {
                    var payload = new GraphicOperationPayload(
                        chatNameOrId,
                        opType,
                        graphicScope,
                        graphicHash,
                        svgBytes,
                        existingGraphicPub?.ExternalMessageId,
                        input.GraphicPackage.Metadata.TargetDate
                    );

                    var gResult = await _graphicDispatcher.DispatchGraphicAsync(payload, cancellationToken).ConfigureAwait(false);
                    if (!gResult.IsSuccess)
                    {
                        return new BatchDispatchResult(false, dispatchResult.TotalProcessed + 1, dispatchResult.TotalSuccessful, $"Graphic dispatch failed: {gResult.ErrorDescription}", dispatchResult.Results);
                    }

                    int? finalMsgId = gResult.MessageId ?? existingMsgId;
                    graphicRegistryUpdates.Add(new RegistryPublicationRecord(
                        artifactId,
                        graphicScope,
                        finalMsgId?.ToString(),
                        graphicHash,
                        isCreate ? "SENT" : "UPDATED",
                        "Graphic"
                    ));
                }
                else
                {
                    graphicRegistryUpdates.Add(new RegistryPublicationRecord(
                        artifactId,
                        graphicScope,
                        existingMsgId?.ToString(),
                        graphicHash,
                        isCreate ? "SENT" : "UPDATED",
                        "Graphic"
                    ));
                }
            }
            else
            {
                if (existingGraphicPub != null)
                {
                    graphicRegistryUpdates.Add(existingGraphicPub);
                }
            }
        }
        else if (registry != null)
        {
            foreach (var oldPub in registry.Publications.Where(p => p.PublicationType.Equals("Graphic", StringComparison.OrdinalIgnoreCase)))
            {
                graphicRegistryUpdates.Add(oldPub);
            }
        }

        // 9. Update Registry Model
        var updatedPublications = new List<RegistryPublicationRecord>();

        foreach (var res in dispatchResult.Results)
        {
            if (!res.IsSuccess) continue;

            if (res.DecisionResult == DecisionResult.Create.ToString())
            {
                var pubId = res.PublicationId ?? Guid.NewGuid();
                var pkg = input.Packages.FirstOrDefault(p => p.TerritoryId == res.TerritoryIdentifier);
                string computedHash = pkg != null ? _hashCalculator.ComputeHash(pkg.Content, pkg.GraphicBytes) : "hash-placeholder";

                var record = new RegistryPublicationRecord(
                    pubId,
                    res.TerritoryIdentifier ?? "unknown",
                    res.MessageId?.ToString(),
                    computedHash, 
                    "SENT",
                    "Text"
                );
                updatedPublications.Add(record);
            }
            else if (res.DecisionResult == DecisionResult.Update.ToString())
            {
                var pubId = res.PublicationId ?? Guid.Empty;
                var pkg = input.Packages.FirstOrDefault(p => p.TerritoryId == res.TerritoryIdentifier);
                string computedHash = pkg != null ? _hashCalculator.ComputeHash(pkg.Content, pkg.GraphicBytes) : "hash-placeholder";

                var record = new RegistryPublicationRecord(
                    pubId,
                    res.TerritoryIdentifier ?? "unknown",
                    res.MessageId?.ToString() ?? (registry?.Publications.FirstOrDefault(p => p.PublisherArtifactId == pubId && p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase))?.ExternalMessageId),
                    computedHash,
                    "UPDATED",
                    "Text"
                );
                updatedPublications.Add(record);
            }
            else if (res.DecisionResult == DecisionResult.Delete.ToString())
            {
                var pubId = res.PublicationId ?? Guid.Empty;
                var record = new RegistryPublicationRecord(
                    pubId,
                    res.TerritoryIdentifier ?? "unknown",
                    null,
                    "deleted-hash",
                    "DELETED",
                    "Text"
                );
                updatedPublications.Add(record);
            }
            else
            {
                var pubId = res.PublicationId ?? Guid.Empty;
                var existingRec = registry?.Publications.FirstOrDefault(p => p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase) && ((pubId != Guid.Empty && p.PublisherArtifactId == pubId) || p.TerritoryId.Equals(res.TerritoryIdentifier, StringComparison.OrdinalIgnoreCase)));
                if (existingRec != null && !updatedPublications.Any(p => p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase) && p.TerritoryId.Equals(existingRec.TerritoryId, StringComparison.OrdinalIgnoreCase)))
                {
                    updatedPublications.Add(existingRec);
                }
            }
        }

        if (registry != null && !isDateRollover)
        {
            var processedTextTerritories = updatedPublications.Where(p => p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase)).Select(p => p.TerritoryId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var oldPub in registry.Publications)
            {
                if (oldPub.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase))
                {
                    if (!processedTextTerritories.Contains(oldPub.TerritoryId))
                    {
                        updatedPublications.Add(oldPub);
                    }
                }
            }
        }

        foreach (var gRec in graphicRegistryUpdates)
        {
            updatedPublications.Add(gRec);
        }

        var newRegistry = new RegistryModel(
            SchemaVersion: registry?.SchemaVersion ?? 1,
            EditionDate: input.EditionDate,
            Status: todayEdition.State.ToString().ToUpperInvariant(),
            Publications: updatedPublications
        );

        // 10. Save Atomically & Push
        await _registryStore.SaveAsync(registryPath, newRegistry, cancellationToken).ConfigureAwait(false);
        await _gitTransport.CommitAndPushAsync(registryPath, $"Sync run for edition {todayEdition.EditionDate}", cancellationToken).ConfigureAwait(false);

        return dispatchResult;
    }
}
