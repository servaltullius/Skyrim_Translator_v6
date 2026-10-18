using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>One generation call the translation run made, written out for the agent app's model to answer.</summary>
/// <param name="Hash">The call's identity: the model profile, instructions, messages and response schema.</param>
/// <param name="Attempt">How many earlier calls in the same run had this hash (a retry of an answer the run rejected).</param>
public sealed record AgentRequest(string Hash, int Attempt, string ModelName, string? Purpose, string SystemInstruction,
    IReadOnlyList<AgentMessage> Messages, string? ResponseMimeType, string? ResponseSchema)
{
    public int Chars => SystemInstruction.Length + Messages.Sum(m => m.Text.Length);

    public static AgentRequest From(string modelName, GeminiGenerateContentRequest request, int attempt = 0)
    {
        var system = string.Join("\n", request.SystemInstruction?.Parts.Where(p => p.Thought != true).Select(p => p.Text) ?? Array.Empty<string>());
        var messages = request.Contents
            .Select(c => new AgentMessage(c.Role ?? "user", string.Join("\n", c.Parts.Where(p => p.Thought != true).Select(p => p.Text))))
            .ToList();
        var mime = request.GenerationConfig?.ResponseMimeType;
        var schema = request.GenerationConfig?.ResponseSchema?.GetRawText();
        var identity = new StringBuilder().Append(modelName).Append('\0').Append(system).Append('\0');
        foreach (var message in messages)
        {
            identity.Append(message.Role).Append('\0').Append(message.Text).Append('\0');
        }

        identity.Append(mime).Append('\0').Append(schema);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))[..32].ToLowerInvariant();
        return new AgentRequest(hash, attempt, modelName, request.Purpose, system, messages, mime, schema);
    }
}

public sealed record AgentMessage(string Role, string Text);
