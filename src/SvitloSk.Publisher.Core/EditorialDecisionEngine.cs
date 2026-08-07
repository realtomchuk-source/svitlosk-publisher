// Source: EDITORIAL_DECISION_ENGINE.md
// Section: 01

using System;

namespace SvitloSk.Publisher.Core;

public class EditorialDecisionEngine : IEditorialDecisionEngine
{
    public EditorialDecision Process(ReasonedConclusion conclusion)
    {
        if (conclusion == null) throw new ArgumentNullException(nameof(conclusion));

        var evaluation = conclusion.Evaluation?.ToUpperInvariant() ?? string.Empty;
        var condition = conclusion.LogicalCondition?.ToUpperInvariant() ?? string.Empty;

        // Map based on Editorial Reasoning Model output (Justifications)
        if (evaluation.Contains("CREATE") || evaluation == "SIGNIFICANT (NEW TERRITORY)")
            return new EditorialDecision(DecisionResult.CREATE, Classification.Persistent);

        if (evaluation.Contains("UPDATE") || evaluation == "SIGNIFICANT (CHANGED CONTENT)")
            return new EditorialDecision(DecisionResult.UPDATE, Classification.Persistent);

        if (evaluation.Contains("DELETE") || evaluation == "SIGNIFICANT (EXPIRED MESSAGE)")
            return new EditorialDecision(DecisionResult.DELETE, Classification.Ephemeral);

        if (evaluation == "SIGNIFICANT (MISSING TERRITORY)")
        {
            if (condition.Contains("EPHEMERAL"))
                return new EditorialDecision(DecisionResult.DELETE, Classification.Ephemeral);
            return new EditorialDecision(DecisionResult.KEEP, Classification.Persistent);
        }

        if (evaluation.Contains("KEEP"))
            return new EditorialDecision(DecisionResult.KEEP, Classification.Persistent);

        if (evaluation.Contains("PROMOTE") || evaluation == "SIGNIFICANT (TOMORROW VISIBLE)")
            return new EditorialDecision(DecisionResult.PROMOTE, Classification.Ephemeral);

        if (evaluation.Contains("GENERATE") || evaluation.Contains("REGENERATE") || evaluation == "SIGNIFICANT (SCHEDULE CHANGED)")
            return new EditorialDecision(DecisionResult.GENERATE, Classification.Persistent);

        if (evaluation.Contains("OPEN"))
            return new EditorialDecision(DecisionResult.OPEN, Classification.Persistent);

        if (evaluation.Contains("CLOSE"))
            return new EditorialDecision(DecisionResult.CLOSE, Classification.Persistent);

        if (evaluation.Contains("REMOVE EPHEMERAL") || evaluation.Contains("CLEANUP"))
            return new EditorialDecision(DecisionResult.REMOVE_EPHEMERAL, Classification.Ephemeral);

        if (evaluation.Contains("IGNORE") || evaluation == "INSIGNIFICANT")
            return new EditorialDecision(DecisionResult.NO_ACTION, Classification.Persistent);

        // Fallback for valid/not valid triggers
        if (evaluation.Contains("VALID"))
            return new EditorialDecision(DecisionResult.NO_ACTION, Classification.Persistent);

        // Default safety
        return new EditorialDecision(DecisionResult.NO_ACTION, Classification.Persistent);
    }
}
