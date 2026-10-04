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

    [Fact]
    public void Save_NeverWritesTheKeyInPlainText()
    {
        var store = new AppSettingsStore(SettingsPath);
        store.Save(new AppSettings(ApiKey: "plain-key-123", ApiKeys: new[] { new SavedApiKey("second", "other-key-456") }));

        var text = File.ReadAllText(SettingsPath);

        Assert.DoesNotContain("plain-key-123", text);
        Assert.DoesNotContain("other-key-456", text);
        Assert.Contains("dpapi:", text);
        var loaded = new AppSettingsStore(SettingsPath).Load();
        Assert.Equal("plain-key-123", loaded.ApiKey);
        Assert.Equal("other-key-456", Assert.Single(loaded.ApiKeys!).ApiKey);
    }

    [Fact]
    public void LegacyPlainTextKey_IsReadAndRewrittenProtected()
    {
        File.WriteAllText(SettingsPath, "{\"apiKey\":\"legacy-key-789\",\"apiKeys\":[{\"name\":\"old\",\"apiKey\":\"legacy-key-000\"}]}");

        var loaded = new AppSettingsStore(SettingsPath).Load();

        Assert.Equal("legacy-key-789", loaded.ApiKey);
        Assert.Equal("legacy-key-000", Assert.Single(loaded.ApiKeys!).ApiKey);
        var text = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("legacy-key-789", text);
        Assert.DoesNotContain("legacy-key-000", text);
    }

    [Fact]
    public void DeleteApiKey_RemovesOnlyTheMainKey()
    {
        var store = new AppSettingsStore(SettingsPath);
        store.Save(new AppSettings(ApiKey: "main-key", ApiKeys: new[] { new SavedApiKey("spare", "spare-key") }, BatchSize: 7));

        store.DeleteApiKey();

        var loaded = new AppSettingsStore(SettingsPath).Load();
        Assert.Null(loaded.ApiKey);
        Assert.Equal("spare-key", Assert.Single(loaded.ApiKeys!).ApiKey);
        Assert.Equal(7, loaded.BatchSize);
    }

    /// <summary>
    /// A key protected under another Windows account or PC cannot be decrypted here. It was dropped without a word,
    /// so the user saw an empty key box and no reason; the next save then erased it.
    /// </summary>
    [Fact]
    public void KeyThatCannotBeDecrypted_IsReported()
    {
        File.WriteAllText(SettingsPath, "{\"apiKeyProtected\":\"dpapi:AAAAAAAA\",\"apiKeys\":[{\"name\":\"x\",\"apiKeyProtected\":\"dpapi:AAAAAAAA\"}],\"batchSize\":9}");
        var store = new AppSettingsStore(SettingsPath);

        var loaded = store.Load();

        Assert.Null(loaded.ApiKey);
        Assert.Equal(9, loaded.BatchSize);
        Assert.NotNull(store.LoadWarning);
        Assert.Contains("API 키 2개", store.LoadWarning);
    }
}
