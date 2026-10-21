using System.Diagnostics;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Monitoring;

public sealed partial class CpuSensorSource(ILogger<CpuSensorSource> logger) : ICpuSensorSource, IDisposable
{
    public const string ProviderId = "platform.windows.cpu-counters";
    public const string Category = "Processor Information";
    public const string Instance = "_Total";
    public const string PerformanceCounterName = "% Processor Performance";
    public const string FrequencyCounterName = "Processor Frequency";
    public const string LimitCounterName = "% Performance Limit";
    public const string Source = "Performance counters Processor Information(_Total)";

    private readonly Lock _lock = new();
    private Slot? _performance;
    private Slot? _frequency;
    private Slot? _limit;
    private bool _readFailureLogged;
    private bool _disposed;

    public CpuTelemetry Sample()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var (performance, frequency, limit, justOpened) = Open();
            var performancePercent = Read(performance);
            return CpuTelemetryCalculator.Compute(justOpened ? null : performancePercent, Read(frequency), Read(limit), Source);
        }
    }

    public IReadOnlyList<CapabilityReport> DescribeCapabilities()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var (performance, frequency, limit, _) = Open();
            return
            [
                Report(CapabilityIds.CpuFrequency, performance, frequency),
                Report(CapabilityIds.CpuPerformanceLimit, limit),
                new CapabilityReport(CapabilityIds.CpuTemperature, CapabilityScopes.System, CapabilityStatus.NotSupported, ProviderId,
                    ReportStrings.CpuTemperatureNoSource),
            ];
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _performance?.Counter?.Dispose();
            _frequency?.Counter?.Dispose();
            _limit?.Counter?.Dispose();
            _disposed = true;
        }
    }

    private (Slot Performance, Slot Frequency, Slot Limit, bool JustOpened) Open()
    {
        if (_performance is { } performance && _frequency is { } frequency && _limit is { } limit)
            return (performance, frequency, limit, false);
        _performance = OpenCounter(PerformanceCounterName);
        _frequency = OpenCounter(FrequencyCounterName);
        _limit = OpenCounter(LimitCounterName);
        return (_performance, _frequency, _limit, true);
    }

    private Slot OpenCounter(string name)
    {
        PerformanceCounter? counter = null;
        try
        {
            counter = new PerformanceCounter(Category, name, Instance, readOnly: true);
            counter.NextValue();
            return new Slot(name, counter, null);
        }
        catch (Exception ex)
        {
            counter?.Dispose();
            var reason = $"{ex.GetType().Name}: {ex.Message}";
            LogOpenFailed(name, reason);
            return new Slot(name, null, reason);
        }
    }

    private double? Read(Slot slot)
    {
        if (slot.Counter is null)
            return null;
        try
        {
            var value = slot.Counter.NextValue();
            return float.IsFinite(value) ? value : null;
        }
        catch (Exception ex)
        {
            if (!_readFailureLogged)
            {
                _readFailureLogged = true;
                LogReadFailed(slot.Name, $"{ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }
    }

    private static CapabilityReport Report(string capabilityId, params Slot[] slots)
    {
        var errors = slots.Where(s => s.Error is not null).Select(s => $"{s.Name}: {s.Error}").ToList();
        return new CapabilityReport(capabilityId, CapabilityScopes.System,
            errors.Count == 0 ? CapabilityStatus.Supported : CapabilityStatus.NotSupported, ProviderId,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "CPU counter '{Counter}' cannot be opened: {Reason}")]
    private partial void LogOpenFailed(string counter, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CPU counter '{Counter}' cannot be read: {Reason}")]
    private partial void LogReadFailed(string counter, string reason);

    private sealed record Slot(string Name, PerformanceCounter? Counter, string? Error);
}
