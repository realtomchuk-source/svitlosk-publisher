using System;

namespace SvitloSk.Publisher.Core.Domain;

public record Territory(string Identifier)
{
    public string Identifier { get; init; } = !string.IsNullOrWhiteSpace(Identifier) 
        ? Identifier 
        : throw new ArgumentException("Territory Identifier cannot be empty or whitespace.", nameof(Identifier));
}
