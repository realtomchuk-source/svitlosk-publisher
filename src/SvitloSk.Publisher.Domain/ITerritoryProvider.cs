namespace SvitloSk.Publisher.Domain;

public interface ITerritoryProvider
{
    Territory? GetByCanonicalIdentity(string canonicalIdentity);
}
