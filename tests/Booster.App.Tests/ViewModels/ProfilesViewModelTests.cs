using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.App.Tests.ViewModels;

public sealed class ProfilesViewModelTests
{
    [Fact]
    public async Task Load_warnings_are_listed_on_the_screen()
    {
        await using var gaming = new FakeGaming(Repository(new ConfigWarning(ConfigWarningKind.Unreadable, "Competitive.json", "file is locked")));
        var profiles = Create(gaming);

        profiles.Activate();

        Assert.True(profiles.HasLoadWarnings);
        var line = Assert.Single(profiles.LoadWarnings);
        Assert.Contains("Competitive.json", line, StringComparison.Ordinal);
        Assert.Contains("файл не принят, используется встроенный профиль", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Partial_fixes_are_listed_with_their_kind()
    {
        await using var gaming = new FakeGaming(Repository(new ConfigWarning(ConfigWarningKind.InvalidValue, "Competitive.json", "unknown mode")));
        var profiles = Create(gaming);

        profiles.Activate();

        var line = Assert.Single(profiles.LoadWarnings);
        Assert.Equal("Competitive.json: недопустимое значение удалено — unknown mode", line);
    }

    [Fact]
    public async Task Without_warnings_the_block_is_hidden()
    {
        await using var gaming = new FakeGaming(Repository());
        var profiles = Create(gaming);

        profiles.Activate();

        Assert.False(profiles.HasLoadWarnings);
        Assert.Empty(profiles.LoadWarnings);
    }

    private static FakeProfileRepository Repository(params ConfigWarning[] warnings) =>
        new() { Current = new ProfileSet([ProfileFactory.Profile(games: ["cs2"])], warnings) };

    private static ProfilesViewModel Create(FakeGaming gaming) =>
        new(gaming.Profiles, new FakeActionCatalog(), new NoWindows(),
            new SessionViewModel(gaming.Coordinator, gaming.Profiles, NullLogger<SessionViewModel>.Instance),
            NullLogger<ProfilesViewModel>.Instance);

    private sealed class NoWindows : IProcessListSource
    {
        public IReadOnlyList<ProcessEntry> GetWindowedProcesses() => [];
    }
}
