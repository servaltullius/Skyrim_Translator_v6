using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal;

internal static class LqaIssueSorter
{
    public static void Sort(List<LqaIssue> issues)
    {
        issues.Sort(
            static (a, b) =>
            {
                var severityCompare = SeverityWeight(a.Severity).CompareTo(SeverityWeight(b.Severity));
                if (severityCompare != 0)
                {
                    return severityCompare;
                }

                var orderCompare = a.OrderIndex.CompareTo(b.OrderIndex);
                if (orderCompare != 0)
                {
                    return orderCompare;
                }

                return string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase);
            }
        );
    }

    private static int SeverityWeight(string severity)
    {
        if (string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(severity, "Warn", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }
}
