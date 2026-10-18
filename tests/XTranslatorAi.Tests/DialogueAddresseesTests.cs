using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

/// <summary>
/// Serana's lines to the Jarls and to her father were compared with her lines to the player. A title that never names
/// the player, where it calls someone, gives such a line a group of its own; a name does not, since Serana Dialogue
/// Add-On has Serana call the player by name.
/// </summary>
public sealed class DialogueAddresseesTests
{
    private const string Serana = "02002B6C";

    [Theory]
    [InlineData("There you are, father!", "father")]
    [InlineData("My Jarl, how should I put this...", "jarl")]
    [InlineData("My Jarl.", "jarl")]
    [InlineData("Hey, um - My Jarl.", "jarl")]
    [InlineData("So, what do you think of Nazeem - my Jarl?", "jarl")]
    [InlineData("Um. *Ahem* May I... my Jarl?", "jarl")]
    [InlineData("Jarl Elisif, a word?", "jarl")]
    [InlineData("Your Majesty!", "majesty")]
    [InlineData("My father would never have allowed it.", null)]
    [InlineData("I serve my Jarl.", null)]
    [InlineData("Jarl Balgruuf is a fair man.", null)]
    [InlineData("Hey Joshua!", null)]
    public void Find_TakesATitleOnlyWhereItCallsSomeone(string line, string? title)
        => Assert.Equal(title, DialogueAddressees.Find(line));

    [Theory]
    [InlineData("Hey Joshua!", true)]
    [InlineData("Damnit Horizon, did you even see them coming?", true)]
    [InlineData("Sybille, how long did you know Torygg?", true)]
    [InlineData("Thank you, Bjorn.", true)]
    [InlineData("My Jarl.", true)]
    [InlineData("However, don't fail me now.", false)]
    [InlineData("So, how are you doing?", false)]
    [InlineData("There they are!", false)]
    public void CallsSomeone_SeesNamesAndTitles(string line, bool calls)
        => Assert.Equal(calls, DialogueAddressees.CallsSomeone(line));

    /// <summary>
    /// The lines of a topic that call no one go with its title when at least half call it and no one else is called:
    /// Elisif's banter does, a battle topic with one "father!" among its cries does not, and neither does a topic that
    /// calls the player by name. A scene's listener and the player's lines are left alone.
    /// </summary>
    [Fact]
    public void Apply_SharesATopicsTitle_OnlyWhenTheTopicIsSaidToIt()
    {
        var keys = new Dictionary<long, string>
        {
            [1] = Serana, [2] = Serana, [3] = Serana, [4] = Serana,
            [10] = Serana, [11] = Serana, [12] = Serana, [13] = Serana,
            [20] = Serana, [21] = Serana, [22] = Serana,
            [30] = Serana + ">0001326A",
            [40] = Serana,
        };
        var lines = new[]
        {
            Line(1, "My Jarl, how should I put this...", "elisif"),
            Line(2, "My Jarl.", "elisif"),
            Line(3, "So, what do you think of my outfit?", "elisif"),
            Line(4, "So, how are you doing?", "elisif"),
            Line(10, "There you are, father!", "battle"),
            Line(11, "You can't hide from us!", "battle"),
            Line(12, "There they are!", "battle"),
            Line(13, "Oh, you're dead.", "battle"),
            Line(20, "My Jarl.", "named"),
            Line(21, "Hey Joshua!", "named"),
            Line(22, "Shall we go?", "named"),
            Line(30, "Thank you, my Jarl.", "scene"),
            new DialogueAddressees.Line(40, "INFO:RNAM", "My Jarl.", "prompt"),
        };

        var groups = DialogueAddressees.Apply(keys, lines);

        Assert.All(new long[] { 1, 2, 3, 4 }, id => Assert.Equal(Serana + ">@jarl", groups[id]));
        Assert.Equal(Serana + ">@father", groups[10]);
        Assert.All(new long[] { 11, 12, 13 }, id => Assert.Equal(Serana, groups[id]));
        Assert.Equal(Serana + ">@jarl", groups[20]);
        Assert.All(new long[] { 21, 22 }, id => Assert.Equal(Serana, groups[id]));
        Assert.Equal(Serana + ">0001326A", groups[30]);
        Assert.Equal(Serana, groups[40]);
    }

    /// <summary>
    /// End to end with the check: Serana is polite to the Jarl and 반말 with the player, and neither group warns; a
    /// 반말 slip to the Jarl does. "말씀드려야 할지..." (whether) is a polite line trailing off, not 반말.
    /// </summary>
    [Fact]
    public async Task LinesToTheJarl_AreComparedApartFromLinesToThePlayer()
    {
        var rows = new (long Id, string English, string Topic, string Korean)[]
        {
            (1, "Let's go.", "travel", "가자. 서둘러."),
            (2, "I'm tired.", "travel", "피곤해."),
            (3, "Over there.", "travel", "저기 있어."),
            (4, "Watch out.", "travel", "조심해."),
            (10, "My Jarl, how should I put this...", "elisif", "야를님, 이걸 어떻게 말씀드려야 할지..."),
            (11, "My Jarl.", "elisif", "야를님."),
            (12, "So, what do you think of my outfit?", "elisif", "그래서, 제 옷차림은 어떤가요?"),
            (13, "So, how are you doing?", "elisif", "그래, 어떻게 지내시나요?"),
            (14, "Thank you for all you've done, my Jarl.", "elisif", "그동안 해 주신 일에 감사드려요, 야를님."),
            (15, "Knew it, my Jarl.", "elisif", "그럴 줄 알았어, 야를."),
        };
        var lines = rows.Select(row => Line(row.Id, row.English, row.Topic)).ToList();
        var keys = lines.ToDictionary(line => line.Id, _ => Serana);
        var groups = DialogueAddressees.Apply(keys, lines);
        var entries = lines.Select(line => new LqaScanEntry(line.Id, (int)line.Id, null, "INFO:NAM1", StringEntryStatus.Done,
            line.Source ?? "", rows.Single(row => row.Id == line.Id).Korean, Speaker: groups[line.Id])).ToList();

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var warned = Assert.Single(issues, i => i.Code == "speaker_tone_inconsistent");
        Assert.Equal(15, warned.Id);
        Assert.Contains("대부분 존댓말, 이 대사 반말", warned.Message);
    }

    private static DialogueAddressees.Line Line(long id, string english, string topic)
        => new(id, "INFO:NAM1", english, "plugin:topic:" + topic);
}
