using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakePowerPlanProvider : IPowerPlanProvider
{
    public static readonly PowerScheme Balanced = new(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Сбалансированная");

    public static readonly PowerScheme HighPerformance = new(Guid.Parse(ProfileFactory.HighPerformance), "Высокая производительность");

    public static readonly PowerScheme PowerSaver = new(Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a"), "Экономия энергии");

    private readonly List<PowerScheme> _schemes;
    private readonly List<Guid> _setCalls = [];

    public FakePowerPlanProvider(params PowerScheme[] schemes)
    {
        _schemes = schemes.Length == 0 ? [Balanced, HighPerformance] : [.. schemes];
        Active = _schemes[0].Id;
    }

    public Guid Active { get; set; }

    public PowerSource Source { get; set; } = PowerSource.AcPower;

    public IReadOnlyList<Guid> SetCalls => _setCalls.ToArray();

    public void Remove(Guid schemeId) => _schemes.RemoveAll(s => s.Id == schemeId);

    public IReadOnlyList<PowerScheme> ListSchemes() => _schemes.ToArray();

    public Guid GetActiveScheme() => Active;

    public void SetActiveScheme(Guid schemeId)
    {
        if (!_schemes.Exists(s => s.Id == schemeId))
            throw new ArgumentException($"Scheme {schemeId} is not installed.", nameof(schemeId));
        _setCalls.Add(schemeId);
        Active = schemeId;
    }

    public PowerSource GetPowerSource() => Source;
}
