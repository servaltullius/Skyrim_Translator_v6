using System.Diagnostics;
using System.Reflection;
using Mutagen.Bethesda.Strings;
using Noggog;

internal sealed class InventoryLimitException(string code, string detail) : Exception(detail)
{
    internal string Code { get; } = code;
}

internal sealed class InventoryBudget(InventoryReport report) : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private long _lastProgressMs = -2000;
    private long _lastWorkingSetSampleMs = -100;
    private long _lastSampledWorkingSetBytes;
    private long _checkpoints;
    private string _current = "initializing";

    internal void Visit(string path, int depth)
    {
        ++report.VisitedNodeCount;
        if (report.VisitedNodeCount > report.Options.MaxVisited)
            throw new InventoryLimitException("visit_limit", $"Visit limit {report.Options.MaxVisited} exceeded at {path}.");
        if (depth > report.Options.MaxDepth)
            throw new InventoryLimitException("depth_limit", $"Depth limit {report.Options.MaxDepth} exceeded at {path}.");
        Checkpoint(path);
    }

    internal void Checkpoint(string current, bool force = false, bool forceMemoryCheck = false)
    {
        _current = current;
        ++_checkpoints;
        if (force || forceMemoryCheck || (_checkpoints & 127) == 0)
        {
            // Retain the managed-heap check at every prior memory checkpoint.
            var managed = GC.GetTotalMemory(forceFullCollection: false);
            report.PeakObservedManagedBytes = Math.Max(report.PeakObservedManagedBytes, managed);
            // On Windows Refresh invalidates ProcessInfo and WorkingSet64 then
            // requests a native process-information snapshot. Bound that cost
            // to one ordinary sample per 100 ms; explicit phase checks bypass it.
            if (force || _elapsed.ElapsedMilliseconds - _lastWorkingSetSampleMs >= 100)
            {
                _process.Refresh();
                _lastSampledWorkingSetBytes = _process.WorkingSet64;
                _lastWorkingSetSampleMs = _elapsed.ElapsedMilliseconds;
                report.PeakObservedWorkingSetBytes = Math.Max(report.PeakObservedWorkingSetBytes, _lastSampledWorkingSetBytes);
            }
            var limit = (long)report.Options.MaxMemoryMb * 1024 * 1024;
            if (_lastSampledWorkingSetBytes > limit || managed > limit)
                throw new InventoryLimitException("memory_limit", $"Last sampled working set {_lastSampledWorkingSetBytes} / current managed {managed} bytes exceeded {limit} bytes at {current}.");
        }
        if (force || _elapsed.ElapsedMilliseconds - _lastProgressMs >= 2000)
            PrintProgress(current);
    }

    internal void PrintProgress(string phase)
    {
        _lastProgressMs = _elapsed.ElapsedMilliseconds;
        Console.Error.WriteLine($"[{_elapsed.Elapsed.TotalSeconds:F1}s] records={report.RecordCount} visited={report.VisitedNodeCount} strings={report.Strings.Count} diagnostics={report.Diagnostics.Count} peakWorkingMiB={report.PeakObservedWorkingSetBytes / 1048576d:F1} phase={phase} last={_current}");
    }

    public void Dispose() => _process.Dispose();
}

internal static class NumericTypes
{
    private static readonly Dictionary<Type, bool> NumericCache = [];
    private static readonly Dictionary<Type, bool> SequenceCache = [];

    internal static bool IsNumericOnly(Type type)
    {
        if (NumericCache.TryGetValue(type, out var cached)) return cached;
        var result = ProveNumeric(type, new HashSet<Type>());
        NumericCache[type] = result;
        return result;
    }

    private static bool ProveNumeric(Type type, HashSet<Type> active)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string) || typeof(ITranslatedStringGetter).IsAssignableFrom(type)) return false;
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)) return true;
        // Color.Name is a computed color identifier, not a plugin string field.
        // Treat the exact framework RGBA value type as numeric by its contract.
        if (type == typeof(System.Drawing.Color)) return true;
        if (!type.IsValueType || type.IsPointer || type.IsByRefLike) return false;
        if (!active.Add(type)) return false; // Unknown recursive layout: do not prune.
        try
        {
            // Inspect storage fields, never computed getters. Noggog.P3Float
            // stores three floats but its Normalized and Absolute properties
            // each create another P3Float, causing an exponential walk if read.
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return fields.Length > 0 && fields.All(field => ProveNumeric(field.FieldType, active));
        }
        finally { active.Remove(type); }
    }

    internal static bool IsNumericSequence(Type type)
    {
        // System.String implements IEnumerable<char>. It is a text terminal,
        // never a numeric collection, including when considered before GetValue.
        if (type == typeof(string) || typeof(ITranslatedStringGetter).IsAssignableFrom(type)) return false;
        if (SequenceCache.TryGetValue(type, out var cached)) return cached;
        var contracts = type.GetInterfaces().Append(type).ToArray();
        // Noggog IReadOnlyArray2d<T> enumerates IKeyValue<P2Int,T>, rather than T.
        // Its fixed key is a numeric coordinate. Only prune the exact array
        // contract when every declared element T is itself proven numeric.
        var arrayElements = contracts
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyArray2d<>))
            .Select(candidate => candidate.GetGenericArguments()[0]).Distinct().ToArray();
        if (arrayElements.Length > 0 && arrayElements.All(IsNumericOnly))
        {
            SequenceCache[type] = true;
            return true;
        }
        var elements = contracts
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(candidate => candidate.GetGenericArguments()[0]).Distinct().ToArray();
        // Only prune a container with a known, entirely numeric element type.
        // object/interface/abstract element types remain eligible for traversal.
        var result = elements.Length > 0 && elements.All(IsNumericOnly);
        SequenceCache[type] = result;
        return result;
    }
}
