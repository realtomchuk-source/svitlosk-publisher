// Source: EDITORIAL_DECISION_ENGINE.md
// Section: 01

namespace SvitloSk.Publisher.Core;

public interface IEditorialDecisionEngine
{
    EditorialDecision Process(ReasonedConclusion conclusion);
}
