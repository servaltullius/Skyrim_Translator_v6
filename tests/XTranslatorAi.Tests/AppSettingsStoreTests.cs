using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

/// <summary>
/// Callers save settings by Load → change → Save. Load returned defaults on any error and Save truncated the file
/// before writing, so a locked or half-written settings.json lost the stored API keys on the next save.
/// </summary>
public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_root, "settings.json");

    public AppSettingsStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Save_ReplacesTheFileWithoutLeavingTemporaryFiles()
    {
        var store = new AppSettingsStore(SettingsPath);
        store.Save(new AppSettings(ApiKey: "key-1", ApiKeys: new[] { new SavedApiKey("main", "key-1") }));
        store.Save(store.Load() with { BatchSize = 20 });

        var loaded = new AppSettingsStore(SettingsPath).Load();

        Assert.Equal(("key-1", 20), (loaded.ApiKey, loaded.BatchSize));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void HalfWrittenFile_IsMovedAsideAndNotOverwritten()
    {
        const string truncated = "{\n  \"apiKeyProtected\": \"dpapi:AQAAANCM";
        File.WriteAllText(SettingsPath, truncated);
        var store = new AppSettingsStore(SettingsPath);

        var loaded = store.Load();
        store.Save(loaded with { BatchSize = 30 });

        Assert.Null(loaded.ApiKey);
        Assert.Contains("corrupt-", store.LoadWarning);
        var backup = Assert.Single(Directory.GetFiles(_root, "settings.json.corrupt-*"));
        Assert.Equal(truncated, File.ReadAllText(backup));
        Assert.Equal(30, new AppSettingsStore(SettingsPath).Load().BatchSize);
    }

    [Fact]
    public void LockedFile_IsNotReplacedWithDefaults()
    {
        var store = new AppSettingsStore(SettingsPath);
        store.Save(new AppSettings(ApiKey: "stored-key"));
        var before = File.ReadAllBytes(SettingsPath);

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = store.Load();
            Assert.Null(loaded.ApiKey);
            Assert.NotNull(store.LoadWarning);
            Assert.Throws<IOException>(() => store.Save(loaded with { BatchSize = 40 }));
        }

        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        var reread = store.Load();
        Assert.Equal("stored-key", reread.ApiKey);
        Assert.Null(store.LoadWarning);
        store.Save(reread with { BatchSize = 40 });
        Assert.Equal(("stored-key", 40), (store.Load().ApiKey, store.Load().BatchSize));
    }

    [Fact]
    public void MissingFile_StartsFromDefaultsWithoutAWarning()
    {
        var store = new AppSettingsStore(SettingsPath);

        Assert.Equal(new AppSettings(), store.Load());
        Assert.Null(store.LoadWarning);
        store.Save(new AppSettings(BatchSize: 5));
        Assert.Equal(5, store.Load().BatchSize);
    }
}
