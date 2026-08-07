using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Domain;

public enum EditionState
{
    Created,
    Active,
    Closed
}

public class Edition
{
    public Guid Id { get; }
    public DateOnly TargetDate { get; }
    public EditionState State { get; private set; }
    
    private readonly List<PublicationPackage> _packages = new();
    public IReadOnlyCollection<PublicationPackage> Packages => _packages.AsReadOnly();

    public Edition(Guid id, DateOnly targetDate)
    {
        Id = id;
        TargetDate = targetDate;
        State = EditionState.Created;
    }

    public void Activate()
    {
        if (State == EditionState.Closed)
        {
            throw new InvalidOperationException("Cannot activate a closed edition.");
        }
        State = EditionState.Active;
    }

    public void Close()
    {
        State = EditionState.Closed;
    }

    public void AddPackage(PublicationPackage package)
    {
        if (State == EditionState.Closed)
        {
            throw new InvalidOperationException("Cannot add package to a closed edition.");
        }
        _packages.Add(package);
    }

    public void RemovePackage(PublicationPackage package)
    {
        if (State == EditionState.Closed)
        {
            throw new InvalidOperationException("Cannot remove package from a closed edition.");
        }
        _packages.Remove(package);
    }
}
