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

        var geminiHttp = FindInChain<GeminiHttpException>(ex);
        if (geminiHttp != null)
        {
            return ClassifyGeminiHttp(geminiHttp);
        }

        var gemini = FindInChain<GeminiException>(ex);
        if (gemini != null)
        {
            var msg = gemini.Message ?? "";
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

        if (FindInChain<IOException>(ex) != null || FindInChain<UnauthorizedAccessException>(ex) != null)
        {
            return new UserFacingError(
                "E410",
                "파일을 읽거나 쓰지 못했습니다. 파일이 다른 프로그램에서 사용 중인지 확인하세요.",
                DetailsInApiLogs: false
            );
        }

        var msgChain = string.Join(" | ", EnumerateExceptionMessages(ex));
        return ClassifyMessageChain(msgChain);
    }

    private static UserFacingError ClassifyMessageChain(string msgChain)
    {
        if (ContainsAny(msgChain, "HTTP 429", "RESOURCE_EXHAUSTED", "rate limit", "too many requests", "429"))
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
                " 401",
                " 403"
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
                "xt_token_leak"
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
                "missing candidates",
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
        if (status == 429)
        {
            return new UserFacingError(
                "E202",
                "요청이 너무 많습니다(요청 제한). 잠시 후 다시 시도하거나 Parallel/Batch를 줄여보세요.",
                DetailsInApiLogs: true
            );
        }

        if (status is 401 or 403)
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

        if (status == 400 && ContainsAny(http.Message ?? "", "API key", "key", "invalid"))
        {
            return new UserFacingError(
                "E201",
                "API 키가 유효하지 않거나 권한이 없습니다. 고급 설정에서 Gemini API 키를 확인하세요.",
                DetailsInApiLogs: true
            );
        }

        return new UserFacingError(
            "E299",
            "요청 처리 중 오류가 발생했습니다. 잠시 후 다시 시도하세요.",
            DetailsInApiLogs: true
        );
    }

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
        foreach (var current in EnumerateExceptions(ex))
        {
            if (current is T found)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception ex)
    {
        Exception? current = ex;
        var depth = 0;
        while (current != null && depth < 6)
        {
            yield return current;
            current = current.InnerException;
            depth++;
        }
    }

    private static IEnumerable<string> EnumerateExceptionMessages(Exception ex)
    {
        foreach (var current in EnumerateExceptions(ex))
        {
            yield return current.Message ?? "";
        }
    }
}
