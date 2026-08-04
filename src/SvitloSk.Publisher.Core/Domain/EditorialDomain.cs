using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Domain;

public enum DecisionType { CREATE, UPDATE, DELETE, SKIP }

public record EditorialDecision(DecisionType Type, string TargetLocality, object Payload);

public record Publication(string Locality, string Content);

public record Edition(string Date, string EmergencyText, List<Publication> Publications);
