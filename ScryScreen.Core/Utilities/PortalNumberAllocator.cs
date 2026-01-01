using System;
using System.Collections.Generic;

namespace ScryScreen.Core.Utilities;

public static class PortalNumberAllocator
{
    public static int GetNextAvailable(IEnumerable<int> usedPortalNumbers)
    {
        if (usedPortalNumbers is null) throw new ArgumentNullException(nameof(usedPortalNumbers));

        var used = new HashSet<int>(usedPortalNumbers);
        var candidate = 1;
        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }
}
