namespace Booster.Core.Abstractions;

public sealed record PowerScheme(Guid Id, string Name);

public enum PowerSource
{
    /// <summary>Сеть; ПК без батареи — тоже здесь.</summary>
    AcPower,
    Battery,
    Unknown,
}

/// <summary>Схемы питания Windows. Booster переключается только между существующими схемами и не меняет их параметры.</summary>
public interface IPowerPlanProvider
{
    IReadOnlyList<PowerScheme> ListSchemes();

    Guid GetActiveScheme();

    /// <exception cref="ArgumentException">Схемы нет в <see cref="ListSchemes"/>; проверяется до вызова API.</exception>
    void SetActiveScheme(Guid schemeId);

    PowerSource GetPowerSource();
}
