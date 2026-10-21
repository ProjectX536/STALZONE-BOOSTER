namespace Booster.Core.Inventory;

/// <summary>Права текущего процесса. Группа Performance Log Users разрешает ETW-захват без повышения.</summary>
public sealed record PrivilegeState(bool IsElevated, bool IsPerformanceLogUser)
{
    public bool CanCaptureFramesWithoutPrompt => IsElevated || IsPerformanceLogUser;
}
