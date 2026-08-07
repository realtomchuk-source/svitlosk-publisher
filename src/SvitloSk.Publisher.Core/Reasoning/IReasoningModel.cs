using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Core.Reasoning;

public interface IReasoningModel
{
    ReasonedConclusion Evaluate(DetectedSituation situation, Edition? edition = null);
}
