namespace Booster.Core.Engine;

public static class CapabilityIds
{
    public const string FrameCapture = "monitoring.frame-capture";
    public const string GpuEngineCounters = "monitoring.gpu-engine-counters";
    public const string DxgiAdapters = "gpu.dxgi-adapters";
    public const string HybridGpuFlags = "gpu.hybrid-flags";
    public const string DisplayConfig = "display.config";
    public const string StorageWmi = "storage.msft-wmi";

    // Область адаптера: CapabilityScopes.Adapter(luid).
    public const string GpuVendorApi = "gpu.vendor-api";
    public const string GpuUtilization = "gpu.telemetry.utilization";
    public const string GpuTemperature = "gpu.telemetry.temperature";
    public const string GpuCoreClock = "gpu.telemetry.core-clock";
    public const string GpuMemoryClock = "gpu.telemetry.memory-clock";
    public const string GpuVram = "gpu.telemetry.vram";
    public const string GpuPower = "gpu.telemetry.power";
    public const string GpuFan = "gpu.telemetry.fan";
    public const string GpuThrottleReasonsSource = "gpu.throttle-reasons";

    // Системная область: CapabilityScopes.System.
    public const string GpuSettingsRead = "gpu.settings.read";
    public const string CpuFrequency = "cpu.telemetry.frequency";
    public const string CpuPerformanceLimit = "cpu.telemetry.performance-limit";
    public const string CpuTemperature = "cpu.telemetry.temperature";
}
