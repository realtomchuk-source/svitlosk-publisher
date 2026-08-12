using System;
using System.Collections.Generic;
using System.Linq;

namespace SvitloSk.Publisher.Application.Orchestration;

public static class CollectionSorter
{
    public static IReadOnlyList<string> SortCollections(IEnumerable<string> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        return items.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
    }
}
