using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Domain;

public class PublicationPackage
{
    public Guid Id { get; }
    public string Name { get; }

    private readonly List<Publication> _publications = new();
    public IReadOnlyCollection<Publication> Publications => _publications.AsReadOnly();

    public PublicationPackage(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public void AddPublication(Publication publication)
    {
        _publications.Add(publication);
    }

    public void RemovePublication(Publication publication)
    {
        _publications.Remove(publication);
    }
}
