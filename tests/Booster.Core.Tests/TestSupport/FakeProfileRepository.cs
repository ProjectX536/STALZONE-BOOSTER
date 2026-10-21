using Booster.Core.Profiles;

namespace Booster.Core.Tests.TestSupport;

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
