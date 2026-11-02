using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public sealed class BundledFranchiseTmSeedService
{
    private readonly string? _globalRootOverride;
    private readonly Func<BethesdaFranchise, string?> _loadBundledSeedText;
    private readonly Func<BethesdaFranchise, string, string?> _loadBundledSeedChangesText;

    public BundledFranchiseTmSeedService(string? globalRootOverride = null, Func<BethesdaFranchise, string?>? loadBundledSeedText = null,
        Func<BethesdaFranchise, string, string?>? loadBundledSeedChangesText = null)
    {
        _globalRootOverride = globalRootOverride;
        _loadBundledSeedText = loadBundledSeedText ?? EmbeddedAssets.LoadBundledFranchiseTmSeed;
        _loadBundledSeedChangesText = loadBundledSeedChangesText ?? EmbeddedAssets.LoadBundledFranchiseTmSeedChanges;
    }

    public async Task EnsureBundledSeedAsync(BethesdaFranchise franchise, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = GetBundledSeedMetadata(franchise);
            if (metadata is null)
            {
                return;
            }

            MoveLegacyStamps(metadata);
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(franchise, _globalRootOverride);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(franchise, metadata.Version, _globalRootOverride);

            // Auto-import moves a successfully imported TSV into imported/. A current stamp
            // without the seed file means this version was already imported; rewriting it
            // would re-import on every load and overwrite user-edited TM rows.
            if (!File.Exists(seedPath) && File.Exists(stampPath)
                && TryParseBundledSeedStamp(await File.ReadAllTextAsync(stampPath, cancellationToken), out var importedStamp)
                && BundledSeedStampMatchesMetadata(importedStamp, metadata))
            {
                return;
            }

            if (await TryWriteChangesSincePreviousAsync(importDir, seedPath, stampPath, metadata, cancellationToken))
            {
                return;
            }

            var seedMatches = await TryValidateOnDiskSeedAsync(seedPath, metadata, cancellationToken);
            if (seedMatches)
            {
                var stampText = File.Exists(stampPath)
                    ? await File.ReadAllTextAsync(stampPath, cancellationToken)
                    : string.Empty;

                if (IsLegacyVersionOnlyStamp(stampText, metadata.Version))
                {
                    await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
                    return;
                }

                if (TryParseBundledSeedStamp(stampText, out var stamp) && BundledSeedStampMatchesMetadata(stamp, metadata))
                {
                    return;
                }

                await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
                return;
            }

            await RewriteSeedAsync(seedPath, stampPath, metadata, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // An open the user stopped stops here too: swallowed, the open went on with the seed half written.
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort only. Bundled seed copy must never block XML loading; a stamp that cannot be moved or a full
            // disk is said in the log, not swallowed on every load.
            AppLog.Write($"WARN 내장 시리즈 TM을 가져오기 폴더에 쓰지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>
    /// The languages of a bundled seed in tm-import, or null for a file the user put there. The seeds are English to
    /// Korean whatever project is open; importing one with a Chinese-source project's languages filed the official
    /// memory as chinese→korean for good.
    /// </summary>
    public static (string Source, string Target)? LanguagesOfSeedFile(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var franchise in Enum.GetValues<BethesdaFranchise>())
        {
            if (GetBundledSeedMetadata(franchise) is { } metadata
                && (string.Equals(metadata.FileName, name, StringComparison.OrdinalIgnoreCase)
                    || IsChangesFileOfSeed(metadata.FileName, name)))
            {
                return ("english", "korean");
            }
        }

        return null;
    }

    // Any version's changes file, not only the current one's: a v7 file the import had not taken yet when 1.16.40 wrote
    // v8's beside it went in with the open project's languages.
    private static bool IsChangesFileOfSeed(string seedFileName, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(seedFileName) + "-v";
        const string suffix = "-changes.tsv";
        return name.Length > stem.Length + suffix.Length
            && name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && name[stem.Length..^suffix.Length].All(char.IsAsciiDigit);
    }

    public static BundledSeedMetadata? GetBundledSeedMetadata(BethesdaFranchise franchise)
        => franchise switch
        {
            // v2 takes the spelling the Korean patch uses most for a source it translates several ways (Ice Storm 얼음 폭풍,
            // not the 얼음의 형상 of two Dawnguard strings); 83 rows changed. v3 takes the patch errors a review found and
            // the game records confirmed (머서 프레이와와 대화하기, Howl of Terror → 피의 자취), and where an Update or DLC
            // override replaces a text, the one the game shows (Lisbet 리스벳, not the 리즈벳 it replaces); 126 rows changed.
            // v4 takes the patch's first review round (화살 - 강철 화살 → 화살 - 강철, 항박일지 → 항해일지); 21 rows changed.
            // v5 takes its second, of names (빛나는 끌개 → 번개 유도장치, 오크제 방어구 → 오크제 전투화 on boots), and the
            // decisions after it (Jumping spiders 도약형, not the 폭발형 of the exploding ones; Pacify 광역 진정; Glowing
            // Spider 발광 거미), and the third, of descriptions (Drains ... Carry Weight 감소, not 흡수), with enchanting
            // 마법부여 in Hearthfire's strings too, and the fourth, of quests (Tolfidr's 1000골드, objectives in ~하기),
            // and the decisions after it (the Augur of Dunlain 던레인의 예언자; the Oil Spider recipe 기름 거미), and the
            // sixth, of the player's dialogue choices (I don't need your advice → 필요 없소), with Gallows Hall, the
            // Creation Club home, 교수대 회관 apart from Gallows Rock's 교수대 바위, and the quests' NPC lines (the
            // Stormcloak oath whole again), and the last batch, of game settings text (Nose Length 코 길이), and the
            // decisions after it (the Companions 컴패니언, the patch's spelling, not 컴패니언즈), and the gallery's own
            // typo reports the review took (Kvenel the Tongue 혀 크베넬), with Majni 마지니 as his eight lines say, and
            // the decisions after the mod list review (Goldenglow Estate 골든글로우 농장, an apiary, not a 지구; the
            // Solitude Fletcher 화살 공방); 573 rows changed.
            // v6 takes the patch's second review of what the game shows all the time, names and descriptions (Iron Door 철문,
            // not the 강철 문 of Steel; the weakness poisons 독 - 마법 약점, not a 마법 저항 that reads as a resistance;
            // Backstab 백스탭; Vignar 비그나 as his lines say), and the decisions after it (Sons of Skyrim 스카이림의 자손들, the
            // name the camps on the map carry; Shalidor's Insights by the perks it applies, duration or magnitude, not both;
            // Burial Urn 유골 단지), and the second review of the quests (Chasing Echoes 메아리를 쫓아서, not 따라오는; the
            // Shrine of Talos 탈로스의 성소 as the note says), and the reading of the books and the quests' dialogue (The
            // Keepers of the Razor 면도칼의 파수꾼, the quest's Razor; Pelagius Wing Key 열쇠 - 펠라지우스 구역; "And that won't
            // work?" 그걸로는 안 되는 거요?, not a 작동하지 않은 that happened before), and the decisions after it (a particle
            // after an <Alias> that takes any name in both forms, <Alias=Victim>을(를); the College 윈터홀드 대학 as the
            // map names it), and the check of the changes no second reader had read (a particle written apart from its
            // alias, <Alias=Location> 에서, joined to it), and the general dialogue a chunk at a time; 463 rows changed (1.15).
            // v7 takes the gallery's reports the first match missed and 도둑 길드 as most of the patch spells it, not
            // 도둑길드; 15 rows changed.
            // v8 takes the patch's third review round: the siege objectives as one (첫 번째 바리케이드를 파괴하기), names and
            // terms as the game and the Korean wiki spell them (Barter 거래, Unbound Dremora 속박되지 않은 드레모라, Uriel 유리엘,
            // the Thieves Guild's Guild Master 길드 마스터), the player's choices in the voice of their conversation, and
            // no-break spaces the Korean font mods lack; Sea Squall is new, so the ship is no longer named with the 스퀄 of
            // Strident Squall (스트라이던트 스퀄 호). And the decisions after it: the player speaks to Jiub, a stranger met in
            // the Soul Cairn, in the 하오체 the patch gives strangers (무슨 일을 한 거요?), and a confirmation box asks
            // ~하겠습니까? as most of them do (이 물품을 강화하겠습니까?). It also drops the CR 18 rows had ended in since v3:
            // in a changes file the CR broke the line and the import dropped the row, so those who came from v1 or v2 never
            // got them (ALTERATION 변화마법), and every upgrade now offers them against the v1/v2 text too. Eight of those
            // take the patch's text now instead of a v3 one it no longer has (Lisbet 리즈벳 again, as all 13 of its strings
            // say; Replace the beacon 등불을 제자리에 놓기); 77 rows changed (1.16.40).
            // v9 takes the text the game shows for the 33 rows where every record with that English shows one text and
            // the seed had another: 31 held the Skyrim.esm string an Update or DLC override replaces, which the reviews
            // never touched (Find the shrine of Boethiah 성소, not 성지; Rescue <Alias=Victim>을(를), as the decision has
            // it), 2 a text the patch no longer has (a skill made Legendary returns its 퍽). It also takes 12 of the player's
            // choices to Septimus, a stranger, in 하오 ('블랙리치'는 어디에 있소?); 45 rows changed (1.16.41).
            // v10 takes the choices a soldier makes when the general discharges them at the council or offers a place,
            // in the 존댓말 the player speaks to a commander (옳다고 생각한 일을 했을 뿐입니다; Sorry, sir 죄송합니다,
            // 장군님); 4 rows changed.
            // v11 takes the patch's spacing review, its rules run on the seed's own text: the dependent nouns apart (있을
            // 거요, 찾을 수), the particles 뿐 and 밖에 joined, 있다 and 않다 apart after -고 and -지 (알고 있소); 237 rows
            // changed (1.16.42).
            // v12 takes the second spacing review the same way: 터, 수, 몇, 때문, 무슨 and 뭘 apart (있을 텐데, 몇 개나),
            // 같다 after a noun and 싶다 after -고 (구더기 같으니, 듣고 싶군); 89 rows changed (1.16.43).
            // v13 takes the third spacing review the same way: 거 the first round missed (그럴 거라고), the negating 안
            // (안 돼), 동안, 대로, 적, 리 and 한 번 when it counts (본 적이, 한 번 더); 96 rows changed (1.16.44).
            // v14 takes the fourth: the dependent noun 지 before a span of time apart (된 지 얼마나), the ending -ㄹ지
            // joined (가능할지도) and 안되다 meaning pitiful joined (안됐구나); 13 rows changed.
            BethesdaFranchise.ElderScrolls => new BundledSeedMetadata(BethesdaFranchise.ElderScrolls, "v14", "bundled-skyrim-tes-franchise-tm.tsv", 1529320, "27d3e1b8d26c8a4a4318f997a29558e1fef2bccce9ce9e6571d929aa0151dcb2",
                PreviousVersions: new[] { "v13", "v12", "v11", "v10", "v9", "v8", "v7", "v6", "v5", "v4", "v3", "v2", "v1" }, ChangesFileName: "bundled-skyrim-tes-franchise-tm-v14-changes.tsv"),
            BethesdaFranchise.Fallout => new BundledSeedMetadata(BethesdaFranchise.Fallout, "v1", "bundled-fallout4-franchise-tm.tsv", 76, "1a4c3068961a75f6245b2392b018c91a6debdc68eb09cb7708d21ac6a3f93c2e"),
            BethesdaFranchise.Starfield => new BundledSeedMetadata(BethesdaFranchise.Starfield, "v1", "bundled-starfield-franchise-tm.tsv", 18094989, "6e3335684e3cd169051820e14684cc7a1165c0ac6c0db9fae71e63a46ca7f47a"),
            _ => null,
        };

    /// <summary>
    /// A user who imported an earlier version gets only the rows that changed since that version: importing the whole
    /// seed again would overwrite every bundled row they edited in the global memory. The newest earlier version they
    /// imported decides which changes they need.
    /// </summary>
    private async Task<bool> TryWriteChangesSincePreviousAsync(string importDir, string seedPath, string stampPath, BundledSeedMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.PreviousVersions is not { } previousVersions || metadata.ChangesFileName is not { } changesFileName
            || File.Exists(stampPath) || File.Exists(seedPath)
            || previousVersions.FirstOrDefault(version => File.Exists(ProjectPaths.GetBundledFranchiseTmSeedStampPath(metadata.Franchise, version, _globalRootOverride)))
                is not { } previous)
        {
            return false;
        }

        var changes = _loadBundledSeedChangesText(metadata.Franchise, previous);
        if (string.IsNullOrWhiteSpace(changes))
        {
            return false;
        }

        Directory.CreateDirectory(importDir);
        await WriteWholeAsync(Path.Combine(importDir, changesFileName), changes, cancellationToken);
        await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
        return true;
    }

    // The stamps were kept in tm-import before; they move next to the global DB (see ProjectPaths).
    private void MoveLegacyStamps(BundledSeedMetadata metadata)
    {
        foreach (var version in new[] { metadata.Version }.Concat(metadata.PreviousVersions ?? Array.Empty<string>()))
        {
            var legacy = ProjectPaths.GetLegacyBundledFranchiseTmSeedStampPath(metadata.Franchise, version, _globalRootOverride);
            var current = ProjectPaths.GetBundledFranchiseTmSeedStampPath(metadata.Franchise, version, _globalRootOverride);
            if (File.Exists(legacy) && !File.Exists(current))
            {
                File.Move(legacy, current);
            }
        }
    }

    private async Task RewriteSeedAsync(string seedPath, string stampPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
    {
        var seedText = _loadBundledSeedText(metadata.Franchise);
        if (string.IsNullOrWhiteSpace(seedText))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
        await WriteWholeAsync(seedPath, seedText, cancellationToken);
        await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
    }

    /// <summary>
    /// Written beside under another name and moved into place: written in place, a seed cut short (an open stopped, a
    /// full disk; the Starfield seed is 18 MB) was imported as it was and moved to imported/. The other name does not end
    /// in .tsv, so the import does not take it.
    /// </summary>
    private static async Task WriteWholeAsync(string path, string text, CancellationToken cancellationToken)
    {
        var partial = Path.ChangeExtension(path, ".partial");
        try
        {
            await File.WriteAllTextAsync(partial, text, cancellationToken);
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static async Task<bool> TryValidateOnDiskSeedAsync(string seedPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
    {
        if (!File.Exists(seedPath))
        {
            return false;
        }

        var seedInfo = new FileInfo(seedPath);
        if (seedInfo.Length != metadata.ExpectedByteLength)
        {
            return false;
        }

        var onDiskSha256 = await ComputeSha256HexAsync(seedPath, cancellationToken);
        return string.Equals(onDiskSha256, metadata.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyVersionOnlyStamp(string stampText, string expectedVersion)
        => string.Equals(stampText.Trim(), expectedVersion, StringComparison.Ordinal);

    private static bool TryParseBundledSeedStamp(string stampText, out BundledSeedStamp stamp)
    {
        stamp = default!;
        var parts = stampText.Trim().Split('|');
        if (parts.Length != 4)
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var expectedByteLength))
        {
            return false;
        }

        if (!long.TryParse(parts[3], out var lastWriteUtcTicks))
        {
            return false;
        }

        stamp = new BundledSeedStamp(parts[0], expectedByteLength, parts[2], lastWriteUtcTicks);
        return true;
    }

    private static bool BundledSeedStampMatchesMetadata(BundledSeedStamp stamp, BundledSeedMetadata metadata)
        => string.Equals(stamp.Version, metadata.Version, StringComparison.Ordinal)
            && stamp.ExpectedByteLength == metadata.ExpectedByteLength
            && string.Equals(stamp.ExpectedSha256, metadata.ExpectedSha256, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Task WriteStampAsync(string stampPath, string seedPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
        => WriteWholeAsync(stampPath, BuildBundledSeedStamp(metadata, File.GetLastWriteTimeUtc(seedPath).Ticks), cancellationToken);

    private static string BuildBundledSeedStamp(BundledSeedMetadata metadata, long lastWriteUtcTicks)
        => string.Join("|", metadata.Version, metadata.ExpectedByteLength, metadata.ExpectedSha256, lastWriteUtcTicks);

    /// <param name="PreviousVersions">Earlier versions, newest first, a user who imported one is upgraded from with only the rows changed since.</param>
    /// <param name="ChangesFileName">The file the rows changed since the user's version are written to.</param>
    public sealed record BundledSeedMetadata(BethesdaFranchise Franchise, string Version, string FileName, long ExpectedByteLength, string ExpectedSha256,
        System.Collections.Generic.IReadOnlyList<string>? PreviousVersions = null, string? ChangesFileName = null);

    private sealed record BundledSeedStamp(string Version, long ExpectedByteLength, string ExpectedSha256, long LastWriteUtcTicks);
}
