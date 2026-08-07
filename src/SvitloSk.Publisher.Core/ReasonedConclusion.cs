// Source: EDITORIAL_REASONING_MODEL.md
// Section: 01.4

using System;

namespace SvitloSk.Publisher.Core;

public record ReasonedConclusion(
    string Evaluation,
    string LogicalCondition
);
