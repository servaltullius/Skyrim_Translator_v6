using System;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static bool IsCachedContentPermissionDenied(Exception ex)
    {
        foreach (var msg in EnumerateExceptionMessages(ex))
        {
            if (msg.IndexOf("cachedcontent", StringComparison.OrdinalIgnoreCase) >= 0
                && (msg.IndexOf("HTTP 403", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCachedContentInvalid(Exception ex)
    {
        foreach (var msg in EnumerateExceptionMessages(ex))
        {
            if (msg.IndexOf("cachedcontent", StringComparison.OrdinalIgnoreCase) >= 0
                && (msg.IndexOf("HTTP 403", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("HTTP 404", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            if (msg.IndexOf("cachedcontents/", StringComparison.OrdinalIgnoreCase) >= 0
                && (msg.IndexOf("HTTP 404", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
            if (msg.IndexOf("cachedcontent", StringComparison.OrdinalIgnoreCase) >= 0
                && (msg.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class PromptCache
    {
        private readonly GeminiClient _gemini;
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly string _systemPrompt;
        private readonly TimeSpan _ttl;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private string? _cachedContentName;
        private bool _disabled;

        public PromptCache(GeminiClient gemini, string apiKey, string modelName, string systemPrompt, TimeSpan ttl)
        {
            _gemini = gemini;
            _apiKey = apiKey;
            _modelName = modelName;
            _systemPrompt = systemPrompt;
            _ttl = ttl;
        }

        public void Invalidate()
        {
            _cachedContentName = null;
        }

        public void Disable()
        {
            _disabled = true;
            _cachedContentName = null;
        }

        public async Task<string?> GetOrCreateAsync(CancellationToken cancellationToken)
        {
            if (_disabled)
            {
                return null;
            }

            var existing = _cachedContentName;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_disabled)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(_cachedContentName))
                {
                    return _cachedContentName;
                }

                var created = await _gemini.CreateCachedContentAsync(_apiKey, _modelName, _systemPrompt, _ttl, cancellationToken);
                _cachedContentName = created;
                return created;
            }
            catch
            {
                _disabled = true;
                _cachedContentName = null;
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DeleteAsync(CancellationToken cancellationToken)
        {
            if (_disabled)
            {
                return;
            }

            var name = _cachedContentName;
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await _gemini.DeleteCachedContentAsync(_apiKey, name!, cancellationToken);
            _cachedContentName = null;
        }
    }
}
