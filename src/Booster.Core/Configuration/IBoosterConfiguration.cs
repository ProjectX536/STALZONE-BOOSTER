namespace Booster.Core.Configuration;

public interface IBoosterConfiguration
{
    BoosterConfigurationSnapshot Current { get; }

    Task SaveUserSettingsAsync(BoosterSettings settings, CancellationToken ct);
}
