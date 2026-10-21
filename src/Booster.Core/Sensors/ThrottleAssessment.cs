using Booster.Core.Abstractions;

namespace Booster.Core.Sensors;

/// <summary>Признак снижения производительности, найденный по замерам датчиков.</summary>
public enum ThrottleSignal
{
    /// <summary>Драйвер GPU сообщил тепловое ограничение частоты.</summary>
    GpuDriverThermal,

    /// <summary>Драйвер GPU сообщил ограничение по мощности.</summary>
    GpuDriverPowerLimit,

    /// <summary>Драйвер GPU сообщил иную причину: источник питания, запрос API, недостаток питания и т.п.</summary>
    GpuDriverOther,

    /// <summary>Температура GPU в пределах запаса от предела, который сообщил драйвер.</summary>
    GpuTemperatureAtLimit,

    /// <summary>Частота ядра под нагрузкой заметно ниже максимума за прогон; причина может быть не тепловой.</summary>
    GpuClockDropUnderLoad,

    /// <summary>Windows сообщила ограничение производительности CPU; причина неизвестна.</summary>
    CpuPerformanceLimited,
}

/// <summary>Насколько признак говорит именно о троттлинге: от прямого ответа драйвера до косвенной оценки.</summary>
public enum ThrottleConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Найденный признак. <see cref="Samples"/> — замеры с признаком, <see cref="TotalSamples"/> — замеры, в которых был этот
/// адаптер (для CPU — все). Peak: максимум температуры, минимум частоты ядра или максимум ограничения CPU — в зависимости
/// от сигнала; <c>Unknown</c>, если нужное значение не измерено.
/// </summary>
public sealed record ThrottleFinding(
    ThrottleSignal Signal,
    ThrottleConfidence Confidence,
    string? AdapterName,
    int Samples,
    int TotalSamples,
    Reading<double> Peak);

/// <summary>
/// Итог анализа прогона. <see cref="EnoughSamples"/> — хватило ли замеров для оценки падения частоты:
/// <see cref="SampleCount"/> не меньше <see cref="RequiredSamples"/>.
/// </summary>
public sealed record ThrottleAssessment(int SampleCount, int RequiredSamples, bool EnoughSamples, IReadOnlyList<ThrottleFinding> Findings);
