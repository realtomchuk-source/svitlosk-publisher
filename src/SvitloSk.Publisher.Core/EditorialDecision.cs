// Source: EDITORIAL_DECISION_ENGINE.md
// Section: 07

using System;

namespace SvitloSk.Publisher.Core;

public record EditorialDecision(
    DecisionResult DecisionResult,
    Classification Classification
);
