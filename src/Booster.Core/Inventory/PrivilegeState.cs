namespace Booster.Core.Inventory;

public sealed record PrivilegeState(bool IsElevated, bool IsPerformanceLogUser)
{
    public bool CanCaptureFramesWithoutPrompt => IsElevated || IsPerformanceLogUser;
}
