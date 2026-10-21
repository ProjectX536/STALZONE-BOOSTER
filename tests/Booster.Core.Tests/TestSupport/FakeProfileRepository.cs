using Booster.Core.Profiles;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Готовый набор профилей без файлов; сохранение не поддерживается.</summary>
public sealed class FakeProfileRepository(params GamingProfile[] profiles) : IProfileRepository
{
    public ProfileSet Current { get; set; } = new(profiles, []);

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task SaveUserChoicesAsync(string profileId, ProfileUserChoices choices, CancellationToken ct) =>
        throw new NotSupportedException();
}
