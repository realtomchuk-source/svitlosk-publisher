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
    
    private readonly List<Publication> _publications = new();
    public IReadOnlyCollection<Publication> Publications => _publications.AsReadOnly();

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

    public void AddPublication(Publication publication)
    {
        if (State == EditionState.Closed)
        {
            throw new InvalidOperationException("Cannot add publication to a closed edition.");
        }
        _publications.Add(publication);
    }

    public void RemovePublication(Publication publication)
    {
        if (State == EditionState.Closed)
        {
            throw new InvalidOperationException("Cannot remove publication from a closed edition.");
        }
        _publications.Remove(publication);
    }
}
