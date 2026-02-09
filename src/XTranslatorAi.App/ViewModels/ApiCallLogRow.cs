using System;

namespace XTranslatorAi.App.ViewModels;

public sealed record ApiCallLogRow(
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    string Provider,
    string Operation,
    string? ModelName,
    string? ApiKeyLabel,
    int? StatusCode,
    bool Success,
    string? ErrorMessage,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    double? CostUsd = null
);
