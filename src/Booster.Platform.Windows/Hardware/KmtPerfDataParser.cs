using Booster.Core.Abstractions;
using Booster.Core.Resources;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Разбор полей <c>D3DKMT_ADAPTER_PERFDATA</c>: ноль означает «драйвер не сообщает», а не реальный ноль.</summary>
public static class KmtPerfDataParser
{
    public const string Source = "d3dkmt.perfdata";

    private const int StatusNotSupported = unchecked((int)0xC00000BB);
    private const double DeciPerUnit = 10;
    private const double HzPerMhz = 1e6;

    /// <param name="temperatureDeciC">Десятые доли °C.</param>
    /// <param name="poweredOn"><c>PowerStateOverride</c>: 1 — адаптер включён, 0 — в энергосбережении.</param>
    /// <param name="caps">null — запрос CAPS не поддержан драйвером.</param>
    public static KmtPerfData Parse(ulong memoryFrequencyHz, uint fanRpm, uint temperatureDeciC, bool poweredOn,
        (uint TemperatureMaxDeciC, uint TemperatureWarningDeciC)? caps)
    {
        var temperature = temperatureDeciC == 0
            ? Reading<double>.Unknown(Source, poweredOn ? ReportStrings.KmtTemperatureNotReported : ReportStrings.KmtAdapterPoweredDown)
            : Reading<double>.Known(temperatureDeciC / DeciPerUnit, Source);

        // TemperatureMax — уровень повреждения, а не начало троттлинга: предел берётся только из TemperatureWarning.
        var limit = caps is { TemperatureWarningDeciC: > 0 } known
            ? Reading<double>.Known(known.TemperatureWarningDeciC / DeciPerUnit, Source)
            : Reading<double>.Unknown(Source, ReportStrings.KmtTemperatureLimitNotReported);

        var fan = fanRpm switch
        {
            0 => Reading<int>.Unknown(Source, ReportStrings.KmtFanZeroRpm),
            > int.MaxValue => Reading<int>.Failure(Source, $"FanRPM out of range: {fanRpm}"),
            _ => Reading<int>.Known((int)fanRpm, Source),
        };

        var memory = memoryFrequencyHz == 0
            ? Reading<double>.Unknown(Source, ReportStrings.KmtMemoryClockNotReported)
            : Reading<double>.Known(memoryFrequencyHz / HzPerMhz, Source);

        return new KmtPerfData(temperature, limit, fan, memory);
    }

    /// <summary>Отказ вызова графического ядра: <c>STATUS_NOT_SUPPORTED</c> — <c>Unsupported</c>, иначе <c>Failure</c>.</summary>
    public static KmtPerfData FromStatus(string function, int status)
    {
        var note = $"{function} failed: NTSTATUS 0x{unchecked((uint)status):X8}";
        return status == StatusNotSupported
            ? new KmtPerfData(Reading<double>.Unsupported(Source, note), Reading<double>.Unsupported(Source, note),
                Reading<int>.Unsupported(Source, note), Reading<double>.Unsupported(Source, note))
            : new KmtPerfData(Reading<double>.Failure(Source, note), Reading<double>.Failure(Source, note),
                Reading<int>.Failure(Source, note), Reading<double>.Failure(Source, note));
    }
}
