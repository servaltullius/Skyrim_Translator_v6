using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>Speech levels decided per speaker and listener are kept in the project and found for a check group.</summary>
public class SpeakerStylesTests
{
    [Fact]
    public async Task Styles_AreKept_Replaced_AndTakenBack()
    {
        var path = Path.Combine(Path.GetTempPath(), "xt-styles-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            await using (var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None))
            {
                await db.SetSpeakerStyleAsync(new SpeakerStyle("04038560", SpeakerStyles.Player, "하오체", "한글패치"), CancellationToken.None);
                await db.SetSpeakerStyleAsync(new SpeakerStyle("04038560", SpeakerStyles.Other, "반말"), CancellationToken.None);
                await db.SetSpeakerStyleAsync(new SpeakerStyle("04038560", SpeakerStyles.Other, "하오체"), CancellationToken.None);
                await db.SetSpeakerStyleAsync(new SpeakerStyle("04018290", SpeakerStyles.Anyone, "하게체"), CancellationToken.None);
                await db.SetSpeakerStyleAsync(new SpeakerStyle("04018290", SpeakerStyles.Anyone, ""), CancellationToken.None);
            }

            await using var reopened = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var styles = await reopened.GetSpeakerStylesAsync(CancellationToken.None);
            Assert.Equal(new[] { ("04038560", "", "하오체", "한글패치"), ("04038560", "00000000", "하오체", (string?)null) },
                styles.Select(s => (s.Speaker, s.Listener, s.Level, s.Note)));
        }
        finally
        {
            ProjectDb.ReleasePool(path);
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("04038560", "하오체")]               // to the player
    [InlineData("04038560>00000000", "반말")]        // another character
    [InlineData("04038560>00000000?", "반말")]       // a guessed listener's group is its listener's
    [InlineData("04038560>0001412A", "해요체")]      // no level for this listener: the level to anyone
    [InlineData("player>04038560", "합쇼체")]
    [InlineData("04018290", null)]
    public void For_FindsThePairsLevel_ElseTheLevelToAnyone(string group, string? expected)
    {
        var styles = SpeakerStyles.ByPair(new[]
        {
            new SpeakerStyle("04038560", SpeakerStyles.Player, "하오"),
            new SpeakerStyle("04038560", SpeakerStyles.Other, "반말"),
            new SpeakerStyle("04038560", SpeakerStyles.Anyone, "해요체"),
            new SpeakerStyle(SpeakerStyles.PlayerSpeaker, "04038560", "합니다체"),
        });

        Assert.Equal(expected, SpeakerStyles.For(styles, group));
    }
}
