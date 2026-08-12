using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Domain;

public class Edition
{
    private readonly List<Publication> _publications = new();

    public string EditionDate { get; }
    public EditionState State { get; private set; }
    public IReadOnlyCollection<Publication> Publications => _publications.AsReadOnly();

    public Edition(string editionDate, EditionState state = EditionState.Planned)
    {
        if (string.IsNullOrWhiteSpace(editionDate))
            throw new ArgumentException("Edition date cannot be empty.", nameof(editionDate));

        EditionDate = editionDate;
        State = state;
    }

    public void AddPublication(Publication publication)
    {
        if (publication == null)
            throw new ArgumentNullException(nameof(publication));

        if (State == EditionState.Closed || State == EditionState.Archived)
            throw new InvalidOperationException("Cannot add publications to a closed or archived edition.");

        _publications.Add(publication);
    }

    public void TransitionTo(EditionState newState)
    {
        if (State == EditionState.Closed && newState == EditionState.Active)
            throw new InvalidOperationException("A closed Edition cannot be reopened.");

        if (State == EditionState.Archived && newState == EditionState.Active)
            throw new InvalidOperationException("Historical Editions cannot be reactivated.");

        State = newState;
    }
}
