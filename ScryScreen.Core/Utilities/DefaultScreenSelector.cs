using System;
using System.Collections.Generic;

namespace ScryScreen.Core.Utilities;

public static class DefaultScreenSelector
{
    /// <summary>
    /// Returns the index of the default screen to use, or -1 if no screens exist.
    /// </summary>
    public static int GetDefaultScreenIndex(IReadOnlyList<bool> isPrimaryByIndex, bool isFirstPortal)
    {
        if (isPrimaryByIndex is null) throw new ArgumentNullException(nameof(isPrimaryByIndex));

        if (isPrimaryByIndex.Count == 0)
        {
            return -1;
        }

        if (isFirstPortal)
        {
            for (var i = 0; i < isPrimaryByIndex.Count; i++)
            {
                if (!isPrimaryByIndex[i])
                {
                    return i;
                }
            }
        }

        return 0;
    }
}
