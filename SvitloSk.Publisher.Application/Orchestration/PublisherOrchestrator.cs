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

public class PublisherOrchestrator : IPublisherOrchestrator
{
    private readonly IRegistryStore _registryStore;
    private readonly IGitTransport _gitTransport;
    private readonly ContentHashCalculator _hashCalculator;
    private readonly EditorialDecisionEngine _decisionEngine;
    private readonly SequentialDispatcher _dispatcher;

    public PublisherOrchestrator(
        IRegistryStore registryStore,
        IGitTransport gitTransport,
        ContentHashCalculator hashCalculator,
        EditorialDecisionEngine decisionEngine,
        SequentialDispatcher dispatcher)
    {
        _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
        _gitTransport = gitTransport ?? throw new ArgumentNullException(nameof(gitTransport));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<BatchDispatchResult> RunOrchestrationAsync(
        string registryPath,
        string chatNameOrId,
        EditorialInput input,
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
            // Try to recover registry from history
            string? restoredJson = await _gitTransport.RestoreFromHistoryAsync(registryPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(restoredJson))
            {
                throw new InvalidOperationException("Registry is corrupted/missing and Git recovery failed.", ex);
            }
            
            // Reload recovered registry
            registry = await _registryStore.LoadAsync(registryPath, cancellationToken).ConfigureAwait(false);
            if (registry == null)
            {
                throw new InvalidOperationException("Restored registry remains invalid.", ex);
            }
        }

        // 2. Map existing RegistryModel to Core Domain Entities
        Edition todayEdition;
        if (registry == null)
        {
            todayEdition = new Edition(input.EditionDate, EditionState.Planned);
        }
        else
        {
            // Verify date match or transition
            var state = Enum.Parse<EditionState>(registry.Status, true);
            todayEdition = new Edition(registry.EditionDate, state);

            foreach (var pubRecord in registry.Publications)
            {
                var pubState = pubRecord.TransmissionState.ToUpperInvariant() switch
                {
                    "SENT" => PublicationState.Published,
                    "UPDATED" => PublicationState.Updated,
                    "DELETED" => PublicationState.Removed,
                    _ => PublicationState.Created
                };
                var type = pubRecord.TransmissionState == "DELETED" ? PublicationType.Tomorrow : PublicationType.Text;
                
                // Map back to Core domain
                var domainPub = new Publication(
                    pubRecord.PublisherArtifactId,
                    pubRecord.TerritoryId,
                    type,
                    DateTime.UtcNow,
                    pubRecord.ContentHash,
                    pubState,
                    IsPersistent: pubRecord.TransmissionState != "DELETED"
                );
                todayEdition.AddPublication(domainPub);
            }
        }

        // 3. Compute Decisions
        var decisions = new List<EditorialDecision>();

        // Decision D-01: Edition Opening
        var openDecision = _decisionEngine.EvaluateEditionOpening(registry == null ? null : todayEdition);
        if (openDecision.DecisionResult == DecisionResult.Open)
        {
            decisions.Add(openDecision);
            todayEdition.TransitionTo(EditionState.Active);
        }

        // Map existing publications by Territory ID for quick lookup
        var existingPubs = todayEdition.Publications.ToDictionary(p => p.TerritoryIdentifier, p => p);

        // Process each incoming territory package
        foreach (var pkg in input.Packages)
        {
            string incomingHash = _hashCalculator.ComputeHash(pkg.Content, pkg.GraphicBytes);
            existingPubs.TryGetValue(pkg.TerritoryId, out var existing);

            // D-02: Validity
            var validity = _decisionEngine.EvaluatePublicationValidity(pkg.TerritoryId, incomingHash, existing);
            decisions.Add(validity);

            // D-03: Create
            var classification = pkg.IsPersistent ? PublicationClassification.Persistent : PublicationClassification.Ephemeral;
            var createDecision = _decisionEngine.EvaluatePublicationCreation(validity, classification);
            if (createDecision.DecisionResult == DecisionResult.Create)
            {
                decisions.Add(createDecision);
            }

            // D-04: Update
            var updateDecision = _decisionEngine.EvaluatePublicationUpdate(validity);
            if (updateDecision.DecisionResult == DecisionResult.Update)
            {
                decisions.Add(updateDecision);
            }

            // D-05: Removal
            var removeDecision = _decisionEngine.EvaluatePublicationRemoval(validity);
            if (removeDecision.DecisionResult == DecisionResult.Delete || removeDecision.DecisionResult == DecisionResult.Keep)
            {
                decisions.Add(removeDecision);
            }
        }

        // D-06: Tomorrow Visibility
        var tomorrowDecision = _decisionEngine.EvaluateTomorrowVisibility(input.TomorrowForecastAvailable);
        if (tomorrowDecision.DecisionResult == DecisionResult.Promote)
        {
            decisions.Add(tomorrowDecision);
        }

        // 4. Dispatch Decisions
        var dispatchResult = await _dispatcher.DispatchAsync(chatNameOrId, decisions, cancellationToken).ConfigureAwait(false);

        if (!dispatchResult.IsSuccess)
        {
            return dispatchResult;
        }

        // 5. Update Registry with Dispatch Results
        var updatedPublications = new List<RegistryPublicationRecord>();
        var finalPubs = todayEdition.Publications.ToDictionary(p => p.PublicationId, p => p);

        // Map dispatcher outcomes back to registry
        foreach (var res in dispatchResult.Results)
        {
            if (!res.IsSuccess) continue;

            if (res.DecisionResult == DecisionResult.Create.ToString())
            {
                var pubId = res.PublicationId ?? Guid.NewGuid();
                var record = new RegistryPublicationRecord(
                    pubId,
                    res.TerritoryIdentifier ?? "unknown",
                    res.MessageId,
                    res.ErrorDescription ?? "hash-placeholder", // TargetHash mapping
                    "SENT"
                );
                updatedPublications.Add(record);
            }
            else if (res.DecisionResult == DecisionResult.Update.ToString())
            {
                var pubId = res.PublicationId ?? Guid.Empty;
                var record = new RegistryPublicationRecord(
                    pubId,
                    res.TerritoryIdentifier ?? "unknown",
                    res.MessageId,
                    res.ErrorDescription ?? "hash-placeholder",
                    "UPDATED"
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
                    "DELETED"
                );
                updatedPublications.Add(record);
            }
        }

        // Keep existing records that weren't mutated in this batch
        var processedIds = updatedPublications.Select(p => p.PublisherArtifactId).ToHashSet();
        if (registry != null)
        {
            foreach (var oldPub in registry.Publications)
            {
                if (!processedIds.Contains(oldPub.PublisherArtifactId))
                {
                    updatedPublications.Add(oldPub);
                }
            }
        }

        var newRegistry = new RegistryModel(
            SchemaVersion: registry?.SchemaVersion ?? 1,
            EditionDate: todayEdition.EditionDate,
            Status: todayEdition.State.ToString().ToUpperInvariant(),
            Publications: updatedPublications
        );

        // 6. Save atomically
        await _registryStore.SaveAsync(registryPath, newRegistry, cancellationToken).ConfigureAwait(false);

        // 7. Commit & Push
        await _gitTransport.CommitAndPushAsync(registryPath, $"Sync run for edition {todayEdition.EditionDate}", cancellationToken).ConfigureAwait(false);

        return dispatchResult;
    }
}
