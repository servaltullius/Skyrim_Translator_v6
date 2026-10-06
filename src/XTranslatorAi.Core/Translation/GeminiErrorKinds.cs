using System;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// What a Gemini HTTP error means, decided from the response itself. Searching every message in an exception
/// chain for "429" or "rate" and "limit" also matched validation errors ("Model output missing id: 1429",
/// "id=4291", "GenerateContent" contains "rate"), which then stopped the whole run as a rate limit.
/// </summary>
internal static class GeminiErrorKinds
{
    // The body also carries the status for a quota error sent with another code. Spent prepaid credits carry it too,
    // but no wait brings them back.
    public static bool IsRateLimit(GeminiHttpException http)
        => !IsCreditsDepleted(http) && (http.StatusCode == 429 || Contains(http.Message, "RESOURCE_EXHAUSTED"));

    // 402 "Your prepayment credits are depleted" (status RESOURCE_EXHAUSTED): the key's project has no prepaid
    // credits left. It was retried and reported as a rate limit.
    public static bool IsCreditsDepleted(GeminiHttpException http)
        => http.StatusCode == 402 || Contains(http.Message, "prepayment credits are depleted");

    // Gemini answers a wrong or expired key with 400 INVALID_ARGUMENT and the reason API_KEY_INVALID, not 401.
    public static bool IsInvalidApiKey(GeminiHttpException http)
        => http.StatusCode is 401 or 403
           || http.StatusCode == 400
           && (Contains(http.Message, "API_KEY_INVALID") || Contains(http.Message, "API key not valid") || Contains(http.Message, "API key expired"));

    private static bool Contains(string? haystack, string needle)
        => haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
