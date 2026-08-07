using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Core.Reasoning;

public class ReasoningModel : IReasoningModel
{
    public ReasonedConclusion Evaluate(DetectedSituation situation, Edition? edition = null)
    {
        return situation.Type switch
        {
            Situation.MorningStartup => new ReasonedConclusion("CREATE", "STARTUP"),
            Situation.CleanupStarted => new ReasonedConclusion("CLEANUP", "CLEANUP_TIME"),
            Situation.EditionClosing => new ReasonedConclusion("CLOSE", "CLOSE_TIME"),
            Situation.ExternalProducerUnavailable => new ReasonedConclusion("IGNORE", "PRODUCER_DOWN"),
            Situation.GraphicUnavailable => new ReasonedConclusion("IGNORE", "GRAPHIC_DOWN"),
            Situation.CommentFlood => new ReasonedConclusion("IGNORE", "COMMENT_FLOOD"),
            Situation.TerritoryAppeared => new ReasonedConclusion("SIGNIFICANT (NEW TERRITORY)", "TERRITORY_APPEARED"),
            Situation.ChangedAddresses => new ReasonedConclusion("SIGNIFICANT (CHANGED CONTENT)", "CONTENT_CHANGED"),
            Situation.TerritoryDisappeared => new ReasonedConclusion("SIGNIFICANT (MISSING TERRITORY)", "EPHEMERAL"),
            Situation.NoChangesDetected => new ReasonedConclusion("INSIGNIFICANT", "NO_CHANGES"),
            _ => new ReasonedConclusion("IGNORE", "DEFAULT")
        };
    }
}
