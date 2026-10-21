using System.Text.Json.Nodes;
using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Core.Tests.Recovery;

public class SessionRecordJsonTests
{
    private readonly InMemoryJournalFileStore _files = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<JournalContents> Load() => new ChangeJournal(_files, NullLogger<ChangeJournal>.Instance).LoadAsync(Ct);

    [Fact]
    public async Task Missing_kind_reads_as_game()
    {
        // Журнал Этапа 2: поля kind ещё нет.
        var json = JsonNode.Parse(SnapshotStore.Serialize(SessionFactory.Session()))!.AsObject();
        Assert.True(json.Remove("kind"));
        _files.Put(SessionFactory.Id + ".json", json.ToJsonString());

        var contents = await Load();

        Assert.Empty(contents.Problems);
        Assert.Equal(SessionKind.Game, Assert.Single(contents.Sessions).Kind);
    }

    [Fact]
    public async Task Preparation_kind_is_written_and_read_back()
    {
        var session = SessionFactory.Session() with { Kind = SessionKind.Preparation };
        var serialized = SnapshotStore.Serialize(session);
        _files.Put(SessionFactory.Id + ".json", serialized);

        var contents = await Load();

        Assert.Contains("\"kind\": \"Preparation\"", serialized);
        Assert.Equal(SessionKind.Preparation, Assert.Single(contents.Sessions).Kind);
    }
}
