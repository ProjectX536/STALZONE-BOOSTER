namespace Booster.Core.Abstractions;

public sealed record PowerScheme(Guid Id, string Name);

public enum PowerSource
{
    AcPower,
    Battery,
    Unknown,
}

public interface IPowerPlanProvider
{
    IReadOnlyList<PowerScheme> ListSchemes();

    Guid GetActiveScheme();

    void SetActiveScheme(Guid schemeId);

    PowerSource GetPowerSource();
}
