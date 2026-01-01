using System;

namespace ScryScreen.Core.Utilities;

public static class PortalSnapshotRules
{
    public static bool ShouldApplyVideoSnapshot(string? assignedMediaFilePath, bool isVideoAssigned, string expectedFilePath)
    {
        if (!isVideoAssigned)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(assignedMediaFilePath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedFilePath))
        {
            return false;
        }

        return string.Equals(assignedMediaFilePath.Trim(), expectedFilePath.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
