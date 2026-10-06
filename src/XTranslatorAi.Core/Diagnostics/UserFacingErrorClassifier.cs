using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Diagnostics;

public readonly record struct UserFacingError(string Code, string Message, bool DetailsInApiLogs);

public static class UserFacingErrorClassifier
{
    public static UserFacingError ClassifyErrorMessage(string? errorMessage)
    {
        var msgChain = (errorMessage ?? "").Trim();
        if (string.IsNullOrWhiteSpace(msgChain))
        {
            return new UserFacingError("E000", "", DetailsInApiLogs: false);
        }

        return ClassifyMessageChain(msgChain);
    }

    public static UserFacingError Classify(Exception ex)
    {
        // Wraps the last 429 or timeout, which alone would only suggest waiting a moment; the run has already stopped.
        if (FindInChain<TranslationRateLimitAbortException>(ex) is { } abort)
        {
            // Not E203: the runner switches keys for E203, and no key helps an overloaded model.
            if (abort.IsServerError)
            {
                return new UserFacingError(
                    "E204",
                    "Gemini 서버 오류(과부하 등)가 계속되어 번역을 멈췄습니다. 남은 행은 대기 상태로 두었으니 잠시 후 이어서 번역하세요.",
                    DetailsInApiLogs: true
                );
            }

            if (abort.IsConnectionFailure)
            {
                return new UserFacingError(
                    "E211",
                    "네트워크 연결 문제나 응답 시간 초과가 계속되어 번역을 멈췄습니다. 남은 행은 대기 상태로 두었으니 연결을 확인한 뒤 이어서 번역하세요.",
                    DetailsInApiLogs: true
                );
            }

            return new UserFacingError(
                "E202",
                "요청 제한이 계속되어 번역을 멈췄습니다(일일 할당량 소진 등). 남은 행은 대기 상태로 두었으니 나중에 이어서 번역하세요.",
                DetailsInApiLogs: true
            );
        }

        if (FindInChain<TaskCanceledException>(ex) != null)
        {
            return new UserFacingError(
                "E210",
                "요청 시간이 초과되었습니다. 잠시 후 다시 시도하거나 Batch/Max chars를 줄여보세요.",
                DetailsInApiLogs: true
            );
        }

        if (ex is OperationCanceledException)
        {
            return new UserFacingError("E000", "작업이 취소되었습니다.", DetailsInApiLogs: false);
        }

        if (FindInChain<InvalidDataException>(ex) is { } notXTranslator && XTranslatorAi.Core.Xml.XTranslatorXmlFormatError.IsFormatError(notXTranslator))
        {
            return new UserFacingError("E431", notXTranslator.Message, DetailsInApiLogs: false);
        }

        // A truncated or hand-edited XML showed as E999 "예상치 못한 오류 … 잠시 후 다시 시도하세요".
        if (FindInChain<System.Xml.XmlException>(ex) is { } xml)
        {
            var where = xml.LineNumber > 0 ? $" {xml.LineNumber}번째 줄 {xml.LinePosition}번째 글자 근처" : "";
            return new UserFacingError(
                "E430",
                $"XML 파일의 형식이 깨져 읽을 수 없습니다{(where.Length == 0 ? "" : "(" + where.Trim() + ")")}. 파일이 중간에 잘렸거나 직접 고치다 태그가 어긋났는지 확인하고, xTranslator에서 다시 내보내세요.",
                DetailsInApiLogs: false
            );
        }

        var geminiHttp = FindInChain<GeminiHttpException>(ex);
        if (geminiHttp != null)
        {
            return ClassifyGeminiHttp(geminiHttp);
        }

        var gemini = FindInChain<GeminiException>(ex);
        if (gemini != null)
        {
            var msg = gemini.Message ?? "";
            if (IsSafetyBlock(msg))
            {
                return SafetyBlocked;
            }

            if (Contains(msg, "MAX_TOKENS"))
            {
                return new UserFacingError(
                    "E310",
                    "응답이 길어 잘렸습니다. Batch/Max chars를 줄이거나 Max out을 늘려보세요.",
                    DetailsInApiLogs: true
                );
            }
        }

        if (FindInChain<HttpRequestException>(ex) != null)
        {
            return new UserFacingError(
                "E211",
                "네트워크 오류입니다. 인터넷 연결을 확인하고 잠시 후 다시 시도하세요.",
                DetailsInApiLogs: true
            );
        }

        if (FindInChain<SqliteException>(ex) != null)
        {
            return new UserFacingError(
                "E420",
                "데이터베이스 오류가 발생했습니다. 앱을 재시작해보세요.",
                DetailsInApiLogs: false
            );
        }

        // A missing or denied file said "다른 프로그램에서 사용 중인지 확인하세요" like a locked one.
        if (FindInChain<FileNotFoundException>(ex) is { } missingFile)
        {
            var name = string.IsNullOrWhiteSpace(missingFile.FileName) ? "" : $": {Path.GetFileName(missingFile.FileName)}";
            return new UserFacingError(
                "E411",
                $"파일을 찾을 수 없습니다{name}. 옮겼거나 지웠는지 확인하세요.",
                DetailsInApiLogs: false
            );
        }

        if (FindInChain<DirectoryNotFoundException>(ex) != null)
        {
            return new UserFacingError(
                "E411",
                "폴더를 찾을 수 없습니다. 옮겼거나 지웠는지 확인하세요.",
                DetailsInApiLogs: false
            );
        }

        if (FindInChain<UnauthorizedAccessException>(ex) != null)
        {
            return new UserFacingError(
                "E412",
                "파일에 접근할 권한이 없습니다. 읽기 전용 파일이거나 Program Files처럼 쓰기가 막힌 폴더인지 확인하세요.",
                DetailsInApiLogs: false
            );
        }

        if (FindInChain<IOException>(ex) != null)
        {
            return new UserFacingError(
                "E410",
                "파일을 읽거나 쓰지 못했습니다. 파일이 다른 프로그램에서 사용 중인지 확인하세요.",
                DetailsInApiLogs: false
            );
        }

        var msgChain = string.Join(" | ", ExceptionTraversal.EnumerateMessages(ex));
        return ClassifyMessageChain(msgChain);
    }

    private static UserFacingError ClassifyMessageChain(string msgChain)
    {
        // Stored row errors are message chains that also carry row ids and token names, so a bare "429" or
        // " 401" would match "Model output missing id: 1429" or "id: 401". Before the rate limit: its body says
        // RESOURCE_EXHAUSTED as well.
        if (ContainsAny(msgChain, "HTTP 402", "prepayment credits are depleted"))
        {
            return CreditsDepleted;
        }

        if (ContainsAny(msgChain, "HTTP 429", "RESOURCE_EXHAUSTED", "rate limit", "too many requests"))
        {
            return new UserFacingError(
                "E202",
                "요청이 너무 많습니다(요청 제한). 잠시 후 다시 시도하거나 Parallel/Batch를 줄여보세요.",
                DetailsInApiLogs: true
            );
        }

        if (ContainsAny(
                msgChain,
                "HTTP 401",
                "HTTP 403",
                "statuscode=401",
                "statuscode=403",
                "unauthorized",
                "forbidden",
                "API_KEY_INVALID",
                "API key not valid",
                "API key expired"
            ))
        {
            return new UserFacingError(
                "E201",
                "API 키가 유효하지 않거나 권한이 없습니다. 고급 설정에서 Gemini API 키를 확인하세요.",
                DetailsInApiLogs: true
            );
        }

        if (ContainsAny(msgChain, "HTTP 500", "HTTP 502", "HTTP 503", "HTTP 504", "HTTP 5"))
        {
            return new UserFacingError(
                "E203",
                "Gemini 서버 오류입니다. 잠시 후 다시 시도하세요.",
                DetailsInApiLogs: true
            );
        }

        // Any other 4xx, as ClassifyGeminiHttp does for a live exception: a 404 for a retired model name or a
        // 400 for a bad parameter fell through to E999, which does not point at the API log that explains it.
        if (Contains(msgChain, "HTTP 4"))
        {
            return new UserFacingError(
                "E299",
                "요청 처리 중 오류가 발생했습니다. 잠시 후 다시 시도하세요.",
                DetailsInApiLogs: true
            );
        }

        if (IsSafetyBlock(msgChain))
        {
            return SafetyBlocked;
        }

        if (ContainsAny(msgChain, "MAX_TOKENS", "output truncated"))
        {
            return new UserFacingError(
                "E310",
                "응답이 길어 잘렸습니다. Batch/Max chars를 줄이거나 Max out을 늘려보세요.",
                DetailsInApiLogs: true
            );
        }

        if (ContainsAny(msgChain, nameof(TaskCanceledException), "timeout", "timed out", "시간이 초과"))
        {
            return new UserFacingError(
                "E210",
                "요청 시간이 초과되었습니다. 잠시 후 다시 시도하거나 Batch/Max chars를 줄여보세요.",
                DetailsInApiLogs: true
            );
        }

        if (ContainsAny(msgChain, nameof(HttpRequestException), "NameResolutionFailure", "DNS", "No route", "connection", "네트워크"))
        {
            return new UserFacingError(
                "E211",
                "네트워크 오류입니다. 인터넷 연결을 확인하고 잠시 후 다시 시도하세요.",
                DetailsInApiLogs: true
            );
        }

        if (ContainsAny(
                msgChain,
                "Missing token in translation",
                "Token sequence mismatch",
                "Unexpected token in translation",
                "Token count mismatch",
                "Missing placeholder token",
                "Missing glossary token",
                "xt_token_leak",
                // The final check: RimImpactOfMob's "体力恢复速度减半" came back with an added "50%" and showed as E999.
                "Protected text mismatch",
                "Unexpected protected text"
            ))
        {
            return new UserFacingError(
                "E330",
                "번역 결과가 토큰/태그 규칙을 위반했습니다. 자동 복구를 켜고 다시 시도하세요.",
                DetailsInApiLogs: false
            );
        }

        if (ContainsAny(
                msgChain,
                "Model output did not contain",
                "Model JSON missing",
                // GeminiClient's texts for a response without a usable answer.
                "no complete candidates",
                "missing final text",
                "incomplete response",
                "missing 'context'",
                "Batch size mismatch",
                "Model output missing id",
                "Model output missing"
            ))
        {
            return new UserFacingError(
                "E320",
                "모델 출력 형식이 예상과 달라 실패했습니다. Batch를 줄이거나 다른 모델로 다시 시도하세요.",
                DetailsInApiLogs: true
            );
        }

        if (ContainsAny(msgChain, "API key is required", "API key"))
        {
            return new UserFacingError(
                "E201",
                "API 키가 유효하지 않거나 권한이 없습니다. 고급 설정에서 Gemini API 키를 확인하세요.",
                DetailsInApiLogs: true
            );
        }

        if (ContainsAny(msgChain, "Project is not loaded", "프로젝트"))
        {
            return new UserFacingError(
                "E101",
                "프로젝트(XML)를 먼저 열어주세요.",
                DetailsInApiLogs: false
            );
        }

        return new UserFacingError(
            "E999",
            "예상치 못한 오류가 발생했습니다. 잠시 후 다시 시도하세요.",
            DetailsInApiLogs: false
        );
    }

    private static UserFacingError ClassifyGeminiHttp(GeminiHttpException http)
    {
        var status = http.StatusCode;
        if (GeminiErrorKinds.IsCreditsDepleted(http))
        {
            return CreditsDepleted;
        }

        if (GeminiErrorKinds.IsRateLimit(http))
        {
            return new UserFacingError(
                "E202",
                "요청이 너무 많습니다(요청 제한). 잠시 후 다시 시도하거나 Parallel/Batch를 줄여보세요.",
                DetailsInApiLogs: true
            );
        }

        if (GeminiErrorKinds.IsInvalidApiKey(http))
        {
            return new UserFacingError(
                "E201",
                "API 키가 유효하지 않거나 권한이 없습니다. 고급 설정에서 Gemini API 키를 확인하세요.",
                DetailsInApiLogs: true
            );
        }

        if (status >= 500 && status <= 599)
        {
            return new UserFacingError(
                "E203",
                "Gemini 서버 오류입니다. 잠시 후 다시 시도하세요.",
                DetailsInApiLogs: true
            );
        }

        return new UserFacingError(
            "E299",
            "요청 처리 중 오류가 발생했습니다. 잠시 후 다시 시도하세요.",
            DetailsInApiLogs: true
        );
    }

    private static readonly UserFacingError CreditsDepleted = new(
        "E205",
        "Gemini API 선불 크레딧이 소진되었다는 응답(HTTP 402)을 받아 번역을 멈췄습니다. AI Studio(ai.studio/projects)에서 이 API 키가 속한 프로젝트의 크레딧·결제를 확인한 뒤 이어서 번역하세요. 남은 행은 대기 상태로 두었습니다.",
        DetailsInApiLogs: true
    );

    // Retrying the row or switching the key sends the same text, which is refused again.
    private static readonly UserFacingError SafetyBlocked = new(
        "E340",
        "Gemini 안전 필터가 요청을 막았습니다(성인 내용 등). 같은 글로 다시 시도해도 막히니 이 행은 직접 번역하세요.",
        DetailsInApiLogs: true
    );

    private static bool IsSafetyBlock(string msg)
        => Contains(msg, GeminiClient.SafetyBlockedText);

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (Contains(haystack, needle))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindInChain<T>(Exception ex) where T : Exception
    {
        foreach (var current in ExceptionTraversal.Enumerate(ex))
        {
            if (current is T found)
            {
                return found;
            }
        }

        return null;
    }

}
