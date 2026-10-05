using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Diagnostics;

namespace XTranslatorAi.Tests;

/// <summary>
/// The runner works on a pool thread and read the window's saved key list there, a collection the window can change
/// at the same time. The keys and the current key are now read on the window's thread.
/// </summary>
public sealed class TranslationRunnerFailoverTests
{
    [Fact]
    public async Task Failover_ReadsTheSavedKeysOnlyInsideTheDispatch()
    {
        var host = new FakeHost();
        var tried = new HashSet<string> { "key-a" };

        var switched = await TranslationRunnerService.TryFailoverToNextSavedGeminiKeyAsync(host, host, tried,
            new UserFacingError("E201", "한도 초과", false));

        Assert.True(switched);
        Assert.Equal(0, host.ReadsOutsideDispatch);
        Assert.Equal("key-b", host.ApiKey);
    }

    private sealed class FakeHost : ITranslationRunnerStatusPort, ITranslationRunnerFailoverPort
    {
        private bool _inDispatch;
        private string _apiKey = "key-a";

        public int ReadsOutsideDispatch { get; private set; }

        public Task DispatchAsync(Action action)
        {
            _inDispatch = true;
            try
            {
                action();
            }
            finally
            {
                _inDispatch = false;
            }

            return Task.CompletedTask;
        }

        public void SetStatusMessage(string message)
        {
        }

        public void SetUserFacingError(string operation, Exception ex)
        {
        }

        public string ApiKey
        {
            get
            {
                Count();
                return _apiKey;
            }
            set => _apiKey = value;
        }

        public bool EnableApiKeyFailover => true;

        public IReadOnlyList<TranslationRunnerSavedApiKey> SavedApiKeys
        {
            get
            {
                Count();
                return new[] { new TranslationRunnerSavedApiKey("A", "key-a"), new TranslationRunnerSavedApiKey("B", "key-b") };
            }
        }

        public void SelectSavedApiKey(TranslationRunnerSavedApiKey savedKey) => _apiKey = savedKey.ApiKey;

        private void Count()
        {
            if (!_inDispatch)
            {
                ReadsOutsideDispatch++;
            }
        }
    }
}
