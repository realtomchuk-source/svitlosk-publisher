namespace SvitloSk.Publisher.Domain;

public interface IPublicationChannel
{
    void Dispatch(Publication artifact);
}
