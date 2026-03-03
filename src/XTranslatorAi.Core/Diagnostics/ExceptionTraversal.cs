using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Diagnostics;

internal static class ExceptionTraversal
{
    internal static IEnumerable<Exception> Enumerate(Exception ex)
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

    internal static IEnumerable<string> EnumerateMessages(Exception ex)
    {
        foreach (var current in Enumerate(ex))
        {
            yield return current.Message ?? "";
        }
    }
}
