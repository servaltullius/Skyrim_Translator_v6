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
        catch
        {
            // Best-effort only. Bundled seed copy must never block XML loading.
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
                    || string.Equals(metadata.ChangesFileName, name, StringComparison.OrdinalIgnoreCase)))
            {
                return ("english", "korean");
            }
        }

        return null;
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
            // work?" 그걸로는 안 되는 거요?, not a 작동하지 않은 that happened before); 324 rows changed.
            BethesdaFranchise.ElderScrolls => new BundledSeedMetadata(BethesdaFranchise.ElderScrolls, "v6", "bundled-skyrim-tes-franchise-tm.tsv", 1528411, "567229948aa3c0490ef590d0b2103f6efbfcf9f0fdcf44543b03f7d4b4d08f6c",
                PreviousVersions: new[] { "v5", "v4", "v3", "v2", "v1" }, ChangesFileName: "bundled-skyrim-tes-franchise-tm-v6-changes.tsv"),
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
        await File.WriteAllTextAsync(Path.Combine(importDir, changesFileName), changes, cancellationToken);
        await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
        return true;
    }

    private async Task RewriteSeedAsync(string seedPath, string stampPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
    {
        var seedText = _loadBundledSeedText(metadata.Franchise);
        if (string.IsNullOrWhiteSpace(seedText))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
        await File.WriteAllTextAsync(seedPath, seedText, cancellationToken);
        await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
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
        => File.WriteAllTextAsync(stampPath, BuildBundledSeedStamp(metadata, File.GetLastWriteTimeUtc(seedPath).Ticks), cancellationToken);

    private static string BuildBundledSeedStamp(BundledSeedMetadata metadata, long lastWriteUtcTicks)
        => string.Join("|", metadata.Version, metadata.ExpectedByteLength, metadata.ExpectedSha256, lastWriteUtcTicks);

    /// <param name="PreviousVersions">Earlier versions, newest first, a user who imported one is upgraded from with only the rows changed since.</param>
    /// <param name="ChangesFileName">The file the rows changed since the user's version are written to.</param>
    public sealed record BundledSeedMetadata(BethesdaFranchise Franchise, string Version, string FileName, long ExpectedByteLength, string ExpectedSha256,
        System.Collections.Generic.IReadOnlyList<string>? PreviousVersions = null, string? ChangesFileName = null);

    private sealed record BundledSeedStamp(string Version, long ExpectedByteLength, string ExpectedSha256, long LastWriteUtcTicks);
}
