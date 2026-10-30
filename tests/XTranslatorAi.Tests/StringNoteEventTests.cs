using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Which of a row's notes each event on the row leaves: every kind on one row, then the event. The rules were spread over
/// several statements (an agent_hold note added in one place needed three more, found by two reviews); these pin what
/// they do, so moving them into one table changes none of it.
/// </summary>
public sealed class StringNoteEventTests : IAsyncLifetime
{
    private const string Reviewed = TranslationConstants.LqaReviewedNoteKindPrefix + "tone_inconsistent";

    private static readonly string[] AllKinds =
    {
        TranslationConstants.TmHitNoteKind, TranslationConstants.GameStringNoteKind, TranslationConstants.PreviousReleaseNoteKind,
        TranslationConstants.TmFallbackNoteKind, TranslationConstants.SourceFixNoteKind, TranslationConstants.AgentHoldNoteKind, Reviewed,
    };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"xt-note-events-{Guid.NewGuid():N}.sqlite");
    private ProjectDb _db = null!;

    public async Task InitializeAsync()
    {
        _db = await ProjectDb.OpenOrCreateAsync(_path, CancellationToken.None);
        await _db.BulkInsertStringsAsync(new[] { StringEntryStatus.Done, StringEntryStatus.Skipped }.Select((status, index) => (
            OrderIndex: index, ListAttr: (string?)null, PartialAttr: (string?)null, AttributesJson: (string?)null,
            Edid: (string?)status.ToString(), Rec: (string?)"MISC:FULL", SourceText: "Aetherial Echo", DestText: "에테르의 메아리",
            Status: status, RawStringXml: "<r/>"
        )).ToArray(), CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        TestDbHelper.TryDeleteDbFiles(_path);
    }

    public static IEnumerable<object[]> Events()
    {
        string[] Without(params string[] kinds) => AllKinds.Except(kinds).ToArray();
        yield return new object[] { "hand edit", StringEntryStatus.Done, Without(TranslationConstants.TmHitNoteKind, TranslationConstants.GameStringNoteKind,
            TranslationConstants.PreviousReleaseNoteKind, TranslationConstants.TmFallbackNoteKind, TranslationConstants.AgentHoldNoteKind) };
        yield return new object[] { "saved done", StringEntryStatus.Done, Without(TranslationConstants.AgentHoldNoteKind) };
        yield return new object[] { "saved error", StringEntryStatus.Done, Without(TranslationConstants.AgentHoldNoteKind) };
        yield return new object[] { "saved skipped", StringEntryStatus.Done, AllKinds };
        yield return new object[] { "batch done", StringEntryStatus.Done, AllKinds };
        yield return new object[] { "batch edited", StringEntryStatus.Done, AllKinds };
        yield return new object[] { "status error", StringEntryStatus.Done, AllKinds };
        yield return new object[] { "retranslate", StringEntryStatus.Done, Array.Empty<string>() };
        yield return new object[] { "release", StringEntryStatus.Skipped, Without(TranslationConstants.AgentHoldNoteKind) };
        // A row finished meanwhile (a lane's submit) is not put back to 대기, but its hold note goes all the same.
        yield return new object[] { "release", StringEntryStatus.Done, Without(TranslationConstants.AgentHoldNoteKind) };
    }

    /// <summary>Every event clears something: one that cleared nothing built "AND ()", which SQLite refuses.</summary>
    [Fact]
    public void EveryEvent_ClearsSomeNotes()
    {
        foreach (var rowEvent in Enum.GetValues<StringNotePolicy.RowEvent>())
        {
            var groups = StringNotePolicy.ClearedBy(rowEvent);
            Assert.True(StringNotePolicy.KindsOf(groups.ToArray()).Count > 0 || groups.Contains(StringNotePolicy.NoteGroup.UserMark), rowEvent.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Events))]
    public async Task EachEvent_LeavesTheNotesItLeft(string happening, StringEntryStatus status, string[] left)
    {
        var id = (await _db.GetStringsAsync(10, 0, CancellationToken.None)).Single(r => r.Status == status).Id;
        foreach (var kind in AllKinds) await _db.UpsertStringNoteAsync(id, kind, "메모", CancellationToken.None);

        switch (happening)
        {
            case "hand edit": await _db.UpdateStringTranslationAsync(id, "고친 번역", StringEntryStatus.Edited, null, CancellationToken.None); break;
            case "saved done": await _db.UpdateStringTranslationAsync(id, "번역", StringEntryStatus.Done, null, CancellationToken.None); break;
            case "saved error": await _db.UpdateStringTranslationAsync(id, "", StringEntryStatus.Error, "오류", CancellationToken.None); break;
            case "saved skipped": await _db.UpdateStringTranslationAsync(id, "번역", StringEntryStatus.Skipped, null, CancellationToken.None); break;
            case "batch done": await _db.UpdateStringTranslationsAsync(new[] { (id, "번역", StringEntryStatus.Done, (string?)null) }, CancellationToken.None); break;
            case "batch edited": await _db.UpdateStringTranslationsAsync(new[] { (id, "번역", StringEntryStatus.Edited, (string?)null) }, CancellationToken.None); break;
            case "status error": await _db.UpdateStringStatusesAsync(new[] { id }, StringEntryStatus.Error, "오류", CancellationToken.None); break;
            case "retranslate": await _db.ResetForRetranslationAsync(new[] { id }, includeEdited: false, CancellationToken.None); break;
            case "release": await _db.ReleaseHeldStringsAsync(new[] { id }, CancellationToken.None); break;
            default: throw new ArgumentOutOfRangeException(nameof(happening));
        }

        Assert.Equal(left.OrderBy(k => k), (await KindsAsync(id)).OrderBy(k => k));
    }

    /// <summary>
    /// Every note kind has its group in the table: a kind added without one was cleared by no event and kept by every
    /// import (the agent_hold note needed four places fixed). The review marks are a prefix of the user's marks.
    /// </summary>
    [Fact]
    public void EveryNoteKind_HasAGroupInThePolicy()
    {
        var kinds = typeof(TranslationConstants).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.EndsWith("NoteKind", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.NotEmpty(kinds);
        Assert.All(kinds, kind => Assert.NotNull(StringNotePolicy.GroupOf(kind)));
        Assert.Equal(StringNotePolicy.NoteGroup.UserMark, StringNotePolicy.GroupOf(Reviewed));
        Assert.Null(StringNotePolicy.GroupOf("no_such_kind"));
        Assert.Equal(new[] { TranslationConstants.GameStringNoteKind, TranslationConstants.PreviousReleaseNoteKind, TranslationConstants.TmHitNoteKind },
            TranslationConstants.KnownTranslationNoteKinds);
    }

    /// <summary>A translation that comes back keeps all its notes, or for a hand edit the user's marks only.</summary>
    [Fact]
    public void ATranslationComingBack_KeepsItsNotes_OrAHandEditsMarksOnly()
    {
        Assert.All(AllKinds, kind => Assert.True(StringNotePolicy.KeptWith(kind, StringEntryStatus.Done)));
        Assert.Equal(new[] { TranslationConstants.SourceFixNoteKind, Reviewed }.OrderBy(k => k),
            AllKinds.Where(kind => StringNotePolicy.KeptWith(kind, StringEntryStatus.Edited)).OrderBy(k => k));
    }

    private async Task<IReadOnlyList<string>> KindsAsync(long id)
    {
        var kinds = new List<string>();
        foreach (var kind in AllKinds.Where(k => k != Reviewed))
        {
            if ((await _db.GetStringNotesByKindAsync(kind, CancellationToken.None)).ContainsKey(id)) kinds.Add(kind);
        }

        if ((await _db.GetLqaReviewedMarksAsync(CancellationToken.None)).Keys.Any(key => key.StringId == id)) kinds.Add(Reviewed);
        return kinds;
    }
}
