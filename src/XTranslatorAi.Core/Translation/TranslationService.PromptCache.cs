using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Diagnostics;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static bool IsCachedContentPermissionDenied(Exception ex)
    {
        foreach (var msg in ExceptionTraversal.EnumerateMessages(ex))
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
        foreach (var msg in ExceptionTraversal.EnumerateMessages(ex))
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

    /// <param name="failedName">The cache the failed request used; null when it used none.</param>
    private static void InvalidatePromptCache(PromptCache cache, string? failedName, Exception ex)
    {
        if (IsCachedContentPermissionDenied(ex))
        {
            cache.Deny(failedName);
        }
        else
        {
            cache.Invalidate(failedName);
        }
    }

    private static async Task<string?> GetPromptCacheNameAsync(PromptCache? cache, CancellationToken cancellationToken)
        => cache != null ? await cache.GetOrCreateAsync(cancellationToken) : null;

    internal sealed class PromptCache
    {
        /// <summary>
        /// Creation attempts after transient failures (429, 5xx, network, timeout) before the run goes on
        /// without the cache. Other failures (the prompt is below the minimum size, the model has no caching)
        /// will not change and disable it at once.
        /// </summary>
        internal const int MaxCreateAttempts = 3;

        private readonly IGeminiClient _gemini;
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly string _systemPrompt;
        private readonly TimeSpan _ttl;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _nameGate = new();

        private string? _cachedContentName;
        private volatile bool _disabled;
        private volatile int _failedCreates;

        public PromptCache(IGeminiClient gemini, string apiKey, string modelName, string systemPrompt, TimeSpan ttl)
        {
            _gemini = gemini;
            _apiKey = apiKey;
            _modelName = modelName;
            _systemPrompt = systemPrompt;
            _ttl = ttl;
        }

        /// <summary>
        /// Forgets the cache only while it is still the one the failed request used. When the cache expired, every
        /// worker whose request used it lands here; a late one used to drop the cache another worker had just
        /// recreated, which then stayed on Google's side until its TTL because DeleteAsync knows only the newest.
        /// </summary>
        public void Invalidate(string? failedName)
        {
            if (failedName == null)
            {
                return;
            }

            lock (_nameGate)
            {
                if (string.Equals(_cachedContentName, failedName, StringComparison.Ordinal))
                {
                    _cachedContentName = null;
                }
            }
        }

        private bool _recreatedAfterDenial;

        /// <summary>
        /// Gemini answers an expired cache (the TTL is never extended) with 403 "CachedContent not found (or permission
        /// denied)", the same as a key that may not use it. Disabling at once ran every request after the first two
        /// hours without the cache. The cache is made again once; a new cache refused as well means it is not allowed.
        /// </summary>
        public void Deny(string? failedName)
        {
            lock (_nameGate)
            {
                // A request that used an older cache: another worker has already dealt with it.
                if (failedName == null || !string.Equals(_cachedContentName, failedName, StringComparison.Ordinal))
                {
                    return;
                }

                if (!_recreatedAfterDenial)
                {
                    _recreatedAfterDenial = true;
                    _cachedContentName = null;
                    return;
                }
            }

            Disable();
        }

        public void Disable()
        {
            _disabled = true;
            lock (_nameGate)
            {
                _cachedContentName = null;
            }
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

            var failuresBeforeWait = _failedCreates;
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

                // Requests that queued up behind a failed attempt go without the cache rather than each
                // retrying at once and using up the attempts in the same moment.
                if (_failedCreates != failuresBeforeWait)
                {
                    return null;
                }

                var created = await _gemini.CreateCachedContentAsync(_apiKey, _modelName, _systemPrompt, _ttl, cancellationToken);
                lock (_nameGate)
                {
                    _cachedContentName = created;
                }
                return created;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One 503 while creating the cache used to turn caching off for the rest of the run.
                // This request goes without the cache; a later one tries again.
                _failedCreates++;
                if (!IsTransientCreateFailure(ex) || _failedCreates >= MaxCreateAttempts)
                {
                    _disabled = true;
                }
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static bool IsTransientCreateFailure(Exception ex)
            => IsRateLimit(ex) || IsServerError(ex)
               || ExceptionTraversal.Enumerate(ex).Any(e => e is HttpRequestException or TaskCanceledException);

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
