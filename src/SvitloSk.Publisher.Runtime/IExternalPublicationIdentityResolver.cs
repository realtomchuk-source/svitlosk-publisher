namespace SvitloSk.Publisher.Runtime;

public interface IExternalPublicationIdentityResolver
{
    void RecordExternalIdentity(string publicationId, string externalId);
    string? ResolveExternalIdentity(string publicationId);
    void RemoveExternalIdentity(string publicationId);
}
