// Source: PUBLISHER_SPECIFICATION.md
// Section: N/A

namespace SvitloSk.Publisher.Core;

public enum DecisionResult { OPEN, CREATE, UPDATE, DELETE, KEEP, PROMOTE, GENERATE, NO_ACTION, REMOVE_EPHEMERAL, CLOSE }
public enum Classification { Persistent, Ephemeral }
