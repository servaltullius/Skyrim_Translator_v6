using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Loqui;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;

return InventoryProgram.Run(args);

internal static class InventoryProgram
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static int Run(string[] args)
    {
        InventoryOptions options;
        try { options = InventoryOptions.Parse(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine("Usage: --plugin FILE --strings-folder DIR --source-encoding NAME --tables-encoding NAME --metadata-encoding NAME --output NEW.json [--language English] [--bsa-folder DIR] [--manifest FILE]");
            return 2;
        }

        var report = new InventoryReport { Options = options };
        using var budget = new InventoryBudget(report);
        var walker = new GetterWalker(report, options.Language, budget);
        var locks = new List<FileStream>();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            budget.Checkpoint("opening inputs", force: true);
            var pluginPath = new ModPath(options.Plugin);
            var resources = ResourcePaths(options, pluginPath.ModKey).ToArray();
            foreach (var resource in resources)
            {
                // These handles are retained throughout import and enumeration;
                // on Windows they prevent writes or replacement of the inputs.
                var stream = new FileStream(resource, FileMode.Open, FileAccess.Read, FileShare.Read);
                locks.Add(stream);
                report.Inputs.Add(new(resource, stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
            }

            var readParameters = new BinaryReadParameters
            {
                Parallel = false,
                ThrowOnUnknownSubrecord = true,
                StringsParam = new StringsReadParameters
                {
                    TargetLanguage = options.Language,
                    StringsFolderOverride = new DirectoryPath(options.StringsFolder),
                    BsaFolderOverride = new DirectoryPath(options.BsaFolder),
                    NonLocalizedEncodingOverride = StrictEncoding(options.SourceEncoding),
                    NonTranslatedEncodingOverride = StrictEncoding(options.MetadataEncoding),
                    EncodingProvider = new FixedEncodingProvider(StrictEncoding(options.TablesEncoding)),
                },
            };

            // A read-only Mutagen overlay; no writer, load order, game detection,
            // app/Core reference, or production field registry is used.
            using var imported = ModFactory.ImportGetter(pluginPath, GameRelease.SkyrimSE, readParameters);
            budget.Checkpoint("imported read-only overlay", force: true);
            if (imported is not ISkyrimModGetter mod)
                throw new InvalidDataException("Mutagen returned a non-Skyrim getter.");
            report.ModKey = mod.ModKey.ToString();
            report.Localized = mod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Localized);
            var masters = mod.ModHeader.MasterReferences.Select(reference => reference.Master).ToArray();
            report.Masters = masters.Select(master => master.ToString()).ToArray();
            var localIndices = new Dictionary<ModKey, int>();
            for (var i = 0; i < masters.Length; ++i)
                if (!localIndices.TryAdd(masters[i], i))
                    throw new InvalidDataException($"Duplicate master: {masters[i]}");
            if (!localIndices.TryAdd(mod.ModKey, masters.Length))
                throw new InvalidDataException("The plugin lists itself as a master.");
            if (masters.Length > 255)
                throw new InvalidDataException("Too many masters for Skyrim's file-local FormID mapping.");

            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in mod.EnumerateMajorRecords())
            {
                if (options.MaxRecords > 0 && report.RecordCount >= options.MaxRecords)
                    throw new InventoryLimitException("record_limit", $"Stopped after requested {options.MaxRecords} records; this is a partial inventory.");
                ++report.RecordCount;
                budget.Checkpoint($"record {record.FormKey}", forceMemoryCheck: true);
                try
                {
                    var recordType = RecordType(record);
                    var formKey = record.FormKey;
                    if (!localIndices.TryGetValue(formKey.ModKey, out var index) || formKey.ID > 0xFFFFFF)
                        throw new InvalidDataException($"Cannot map file-local FormID: {formKey}");
                    var rawId = ((uint)index << 24) | formKey.ID;
                    var identity = new RecordIdentity(recordType, formKey.ToString(), rawId, record.EditorID,
                        record.IsDeleted, unchecked((uint)record.MajorRecordFlagsRaw));
                    var uniqueKey = $"{recordType}/{rawId:X8}";
                    if (!identities.Add(uniqueKey))
                        report.AddDiagnostic(new("error", "duplicate_record", uniqueKey, "Repeated record identity from Mutagen enumeration."));
                    report.RecordsByType[recordType] = report.RecordsByType.GetValueOrDefault(recordType) + 1;
                    walker.WalkRecord(record, identity);
                }
                catch (Exception error) when (Unwrap(error) is not InventoryLimitException)
                {
                    report.AddDiagnostic(new("error", "record_failure", record.FormKey.ToString(), Unwrap(error).ToString()));
                }
            }
            report.EnumerationReachedEnd = true;

            for (var i = 0; i < locks.Count; ++i)
            {
                locks[i].Position = 0;
                var after = Convert.ToHexString(SHA256.HashData(locks[i]));
                if (after != report.Inputs[i].Sha256)
                    report.AddDiagnostic(new("error", "input_changed", report.Inputs[i].Path, "Input changed during inventory."));
            }
            report.InputHashesRechecked = true;
        }
        catch (Exception error)
        {
            report.MarkStopped(Unwrap(error));
        }
        finally
        {
            foreach (var stream in locks) stream.Dispose();
        }

        report.TranslatedStringCount = report.Strings.Count(item => item.Kind == "translated");
        report.PlainStringCount = report.Strings.Count(item => item.Kind == "plain");
        report.NonEmptyTranslatedStringCount = report.Strings.Count(item => item.Kind == "translated" && !string.IsNullOrEmpty(item.Text));
        report.TraversalCompleted = report.StopCode is null && report.EnumerationReachedEnd && report.InputHashesRechecked && report.Diagnostics.All(item => item.Severity != "error");
        if (options.Manifest is not null && report.TraversalCompleted)
        {
            try
            {
                budget.Checkpoint("starting comparison", force: true);
                report.Comparison = InventoryComparison.Compare(options.Manifest, report.Strings, budget, report.Inputs[0].Sha256);
                budget.Checkpoint("completed comparison", force: true);
            }
            catch (Exception error)
            {
                report.MarkStopped(Unwrap(error));
                report.TraversalCompleted = false;
            }
        }
        else if (options.Manifest is not null)
            report.ComparisonSkippedReason = "Traversal failed or stopped early; a partial inventory must not produce a full-manifest comparison.";
        budget.PrintProgress("writing report");
        try
        {
            // CreateNew refuses to overwrite any existing output or input.
            using var output = new FileStream(options.Output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, report, JsonOptions);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Could not create report: {error.Message}");
            return 2;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            options.Output, report.TraversalCompleted, report.RecordCount,
            report.TranslatedStringCount, report.NonEmptyTranslatedStringCount, report.PlainStringCount,
            DiagnosticCount = report.Diagnostics.Count,
            report.VisitedNodeCount, report.StopCode, report.StopDetail,
        }, JsonOptions));
        return report.TraversalCompleted ? 0 : 1;
    }

    private static IEnumerable<string> ResourcePaths(InventoryOptions options, ModKey modKey)
    {
        yield return options.Plugin;
        var prefix = $"{modKey.Name}_{options.Language}.";
        foreach (var path in Directory.EnumerateFiles(options.StringsFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && new[] { ".strings", ".dlstrings", ".ilstrings" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                yield return Path.GetFullPath(path);
        }
        // Explicit archive folder only; record all archives the lookup could use.
        foreach (var path in Directory.EnumerateFiles(options.BsaFolder).Order(StringComparer.OrdinalIgnoreCase))
            if (Path.GetExtension(path).Equals(".bsa", StringComparison.OrdinalIgnoreCase))
                yield return Path.GetFullPath(path);
    }

    private static IMutagenEncoding StrictEncoding(string name) => new MutagenEncodingWrapper(
        Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));

    private static string RecordType(IMajorRecordGetter record)
    {
        if (record is not ILoquiObject loqui)
            throw new InvalidDataException($"Record is not a Loqui object: {record.GetType().FullName}");
        var getter = loqui.Registration.GetterType;
        var direct = getter.GetCustomAttribute<AssociatedRecordTypesAttribute>(inherit: false)?.Types;
        var types = direct ?? getter.GetInterfaces()
            .SelectMany(type => type.GetCustomAttribute<AssociatedRecordTypesAttribute>(inherit: false)?.Types ?? [])
            .Distinct().ToArray();
        if (types.Length != 1)
            throw new InvalidDataException($"Cannot obtain one record signature from {getter.FullName}: {string.Join(",", types.Select(type => type.ToString()))}");
        return types[0].ToString();
    }

    internal static Exception Unwrap(Exception error) => error is TargetInvocationException { InnerException: not null } target
        ? Unwrap(target.InnerException!) : error;
}

internal sealed class FixedEncodingProvider(IMutagenEncoding encoding) : IMutagenEncodingProvider
{
    public IMutagenEncoding GetEncoding(GameRelease release, Language language) => encoding;
}

internal sealed class GetterWalker(InventoryReport report, Language language, InventoryBudget budget)
{
    private readonly Dictionary<Type, PropertyInfo[]> _propertyCache = [];
    private readonly HashSet<object> _active = new(ReferenceEqualityComparer.Instance);

    internal void WalkRecord(IMajorRecordGetter record, RecordIdentity identity)
    {
        _active.Clear();
        Walk(record, identity, "", record, 0);
    }

    private void Walk(object? value, RecordIdentity identity, string path, IMajorRecordGetter root, int depth)
    {
        if (value is null) return;
        budget.Visit($"{identity.RecordType}/{identity.RawFormId:X8}/{path}", depth);
        if (value is ITranslatedStringGetter translated)
        {
            try
            {
                var stringsKey = (translated as IOptionalStringsKeyGetter)?.StringsKey;
                var found = translated.TryLookup(language, out var text);
                var state = string.IsNullOrEmpty(text)
                    ? stringsKey.HasValue ? "keyed_empty" : "unkeyed_empty"
                    : "resolved";
                if (ReferenceEquals(translated, TranslatedString.Empty)
                    && stringsKey is null && translated.String == string.Empty)
                {
                    // Generated absent optional fields can return this shared
                    // sentinel, whose own target language defaults to English.
                    // It has no localized key to resolve in another language.
                    text = string.Empty;
                    state = "empty_default";
                }
                else if (!found)
                {
                    // Pinned StringBinaryTranslation.Parse maps localized ID 0
                    // to TranslatedString(targetLanguage, directString: null),
                    // with no StringsKey. Both lazy and eager nonzero IDs keep
                    // their key, even when lookup fails. Do not turn keyed or
                    // other-language failures into empty values.
                    if (report.Localized == true && translated is TranslatedString
                        && stringsKey is null && translated.TargetLanguage == language
                        && translated.String is null)
                    {
                        state = "localized_unkeyed_null";
                    }
                    else
                    {
                        state = "unresolved";
                        Problem(identity, path, "missing_language", $"Cannot resolve {language}; StringsKey={stringsKey?.ToString("X8") ?? "<none>"}, TargetLanguage={translated.TargetLanguage}.");
                    }
                }
                else if (text is null)
                {
                    state = "unresolved";
                    Problem(identity, path, "invalid_null_lookup", $"Lookup returned true with null text; StringsKey={stringsKey?.ToString("X8") ?? "<none>"}.");
                }
                report.AddString(new(identity, path, "translated", text, value.GetType().FullName!, state, stringsKey));
            }
            catch (Exception error) when (InventoryProgram.Unwrap(error) is not InventoryLimitException)
            { Problem(identity, path, "translated_string_failure", InventoryProgram.Unwrap(error).ToString()); }
            return;
        }
        if (value is string plain)
        {
            report.AddString(new(identity, path, "plain", plain, "System.String"));
            return;
        }
        var type = value.GetType();
        if (NumericTypes.IsNumericOnly(type))
        {
            report.CountPruned(report.PrunedNumericTypes, type);
            return;
        }
        if (NumericTypes.IsNumericSequence(type))
        {
            report.CountPruned(report.PrunedNumericSequenceTypes, type);
            return;
        }
        if (type.IsPrimitive || type.IsEnum || value is decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Type or Version)
            return;
        if (value is FormKey or ModKey or IFormLinkGetter)
            return; // Never resolve links into another record or a user's load order.
        if (value is IMajorRecordGetter && !ReferenceEquals(value, root))
        {
            ++report.NestedMajorRecordEdgesSkipped;
            return; // EnumerateMajorRecords supplies each child independently.
        }
        if (value is byte[] or ReadOnlyMemory<byte> or Memory<byte> || value is IEnumerable<byte>)
            return;
        if (!type.IsValueType && !_active.Add(value))
        {
            Problem(identity, path, "object_cycle", type.FullName!);
            return;
        }
        try
        {
            if (value is IEnumerable sequence)
            {
                var index = 0;
                try
                {
                    foreach (var child in sequence)
                        Walk(child, identity, $"{path}[{index++}]", root, depth + 1);
                }
                catch (Exception error) when (InventoryProgram.Unwrap(error) is not InventoryLimitException)
                { Problem(identity, path, "enumeration_failure", InventoryProgram.Unwrap(error).ToString()); }
                return;
            }

            var getterType = value is ILoquiObject loqui ? loqui.Registration.GetterType : type;
            if (getterType == typeof(IWeatherAmbientColorSetGetter))
            {
                // This pinned canonical schema contains only four AmbientColors
                // objects (seven RGBA colors, a float, and version flags each).
                // Its overlay has unimplemented accessors: report a schema-based
                // exclusion, never pretend those values were decoded successfully.
                const string source = "Mutagen.Bethesda.Skyrim/Records/Major Records/WeatherAmbientColorSet_Generated.cs + Mutagen.Bethesda.Skyrim/Records/Common Subrecords/AmbientColors_Generated.cs @ bdbb6ff6faad3b4c3b0689e98e889ecd205068c8";
                var key = getterType.FullName!;
                if (!report.DeclaredNumericSchemaExclusions.TryGetValue(key, out var exclusion))
                    report.DeclaredNumericSchemaExclusions[key] = exclusion = new("Four TimeOfDay AmbientColors slots contain only RGBA colors, float Scale, and enum Versioning; values were not decoded.", source);
                ++exclusion.Count;
                return;
            }
            // Loqui's generated GetNthObject/GetNthName reflection API is not
            // implemented by these Skyrim classes. Reflect the canonical getter
            // interface instead, including inherited properties and wrappers.
            if (!(value is ILoquiObject) && !(type.Namespace?.StartsWith("Mutagen.") == true)
                && !(type.Namespace?.StartsWith("Noggog") == true)
                && !(type.IsValueType && type.Namespace is "System.Numerics" or "System.Drawing")
                && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
            {
                Problem(identity, path, "unsupported_object", type.FullName!);
                return;
            }
            var properties = Properties(getterType);
            if (properties.Length == 0)
            {
                report.LeafTypesWithoutProperties[type.FullName!] = report.LeafTypesWithoutProperties.GetValueOrDefault(type.FullName!) + 1;
                return;
            }
            // These two pinned weather contracts explicitly implement Item[time]
            // as aliases of Sunrise/Day/Sunset/Night. Visit the finite indexer
            // once and do not also traverse the named aliases.
            var knownWeatherAliases = getterType == typeof(IWeatherImageSpacesGetter)
                || getterType == typeof(IWeatherVolumetricLightingGetter);
            foreach (var property in properties)
            {
                var childPath = path.Length == 0 ? property.Name : path + "." + property.Name;
                if (knownWeatherAliases && Enum.GetNames<TimeOfDay>().Contains(property.Name, StringComparer.Ordinal))
                {
                    ++report.KnownEnumAliasPropertiesSkipped;
                    continue;
                }
                // Decide from declared types before invoking calculated getters.
                // P3Float.Normalized/Absolute and Percent.Inverse return new value
                // instances and otherwise form an exponential reflection tree.
                if (NumericTypes.IsNumericOnly(property.PropertyType))
                {
                    report.CountPruned(report.PrunedNumericTypes, property.PropertyType);
                    continue;
                }
                if (NumericTypes.IsNumericSequence(property.PropertyType))
                {
                    report.CountPruned(report.PrunedNumericSequenceTypes, property.PropertyType);
                    continue;
                }
                if (property.GetIndexParameters().Length != 0)
                {
                    WalkEnumIndexer(value, identity, childPath, root, depth, getterType, properties, property, knownWeatherAliases);
                    continue;
                }
                try { WalkDeclared(property.GetValue(value), property.PropertyType, identity, childPath, root, depth + 1); }
                catch (Exception error) when (InventoryProgram.Unwrap(error) is not InventoryLimitException)
                { Problem(identity, childPath, "property_failure", InventoryProgram.Unwrap(error).ToString()); }
            }
        }
        finally
        {
            if (!type.IsValueType) _active.Remove(value);
        }
    }

    private void WalkDeclared(object? value, Type declaredType, RecordIdentity identity, string path,
        IMajorRecordGetter root, int depth)
    {
        if (value is null && typeof(ITranslatedStringGetter).IsAssignableFrom(declaredType))
        {
            budget.Visit($"{identity.RecordType}/{identity.RawFormId:X8}/{path}", depth);
            // The canonical getter explicitly returned no translated value.
            // This is distinct from an existing keyed value whose lookup failed.
            report.AddString(new(identity, path, "translated", null, declaredType.FullName!, "absent"));
            return;
        }
        Walk(value, identity, path, root, depth);
    }

    private void WalkEnumIndexer(object owner, RecordIdentity identity, string path, IMajorRecordGetter root,
        int depth, Type getterType, PropertyInfo[] properties, PropertyInfo indexer, bool knownWeatherAliases)
    {
        var parameters = indexer.GetIndexParameters();
        if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum
            || parameters[0].ParameterType.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            Problem(identity, path, "unsupported_indexer", getterType.FullName!);
            return;
        }
        var enumType = parameters[0].ParameterType;
        var members = Enum.GetValues(enumType).Cast<object>().Distinct().ToArray();
        if (members.Length > 64)
        {
            Problem(identity, path, "enum_indexer_limit", $"{enumType.FullName} has {members.Length} distinct members.");
            return;
        }
        var names = members.Select(member => Enum.GetName(enumType, member)!).ToArray();
        if (!knownWeatherAliases && properties.Any(property => property != indexer
                && names.Contains(property.Name, StringComparer.Ordinal) && property.PropertyType == indexer.PropertyType))
        {
            Problem(identity, path, "ambiguous_indexer_alias", "Named properties may alias this indexer; omitted indexer traversal requires manual classification, so this run is not complete.");
            return;
        }
        var schemaKey = $"{getterType.FullName}.{indexer.Name}[{enumType.FullName}]";
        if (!report.EnumIndexerSchemas.TryGetValue(schemaKey, out var evidence))
            report.EnumIndexerSchemas[schemaKey] = evidence = new(names, knownWeatherAliases
                ? "Named weather properties are aliases; only Item[TimeOfDay] is traversed. Pinned WeatherImageSpaces.cs / WeatherVolumetricLighting.cs."
                : "All distinct non-flags enum literals are traversed; no same-name typed aliases found.");
        foreach (var member in members)
        {
            var memberPath = $"{path}[{Enum.GetName(enumType, member)}]";
            ++evidence.Attempted;
            try
            {
                WalkDeclared(indexer.GetValue(owner, [member]), indexer.PropertyType, identity, memberPath, root, depth + 1);
                ++evidence.Completed;
            }
            catch (Exception error) when (InventoryProgram.Unwrap(error) is not InventoryLimitException)
            { Problem(identity, memberPath, "enum_indexer_failure", InventoryProgram.Unwrap(error).ToString()); }
        }
    }

    private PropertyInfo[] Properties(Type getter)
    {
        if (_propertyCache.TryGetValue(getter, out var cached)) return cached;
        var candidates = new[] { getter }.Concat(getter.IsInterface ? getter.GetInterfaces() : [])
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(property => property.GetMethod is { IsStatic: false })
            .Where(property => property.Name != "Registration")
            .GroupBy(property => property.Name, StringComparer.Ordinal);
        // For aliases such as INamedGetter.Name(string), use the concrete
        // Skyrim getter's ITranslatedStringGetter property once, not both views.
        var result = candidates.Select(group => group
                .OrderByDescending(property => property.DeclaringType == getter)
                .ThenByDescending(property => typeof(ITranslatedStringGetter).IsAssignableFrom(property.PropertyType))
                .ThenByDescending(property => property.DeclaringType!.GetInterfaces().Length)
                .ThenBy(property => property.DeclaringType!.FullName, StringComparer.Ordinal).First())
            .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        _propertyCache.Add(getter, result);
        return result;
    }

    private void Problem(RecordIdentity identity, string path, string code, string detail) =>
        report.AddDiagnostic(new("error", code, $"{identity.RecordType}/{identity.RawFormId:X8}/{path}", detail));
}

internal sealed record InventoryOptions(string Plugin, string StringsFolder, string BsaFolder,
    string SourceEncoding, string TablesEncoding, string MetadataEncoding, Language Language, string Output, string? Manifest,
    int MaxRecords, long MaxVisited, int MaxDiagnostics, int MaxStrings, long MaxTextChars, int MaxMemoryMb, int MaxDepth)
{
    internal static InventoryOptions Parse(string[] args)
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { "--plugin", "--strings-folder", "--bsa-folder", "--source-encoding", "--tables-encoding", "--metadata-encoding", "--language", "--output", "--manifest", "--max-records", "--max-visited", "--max-diagnostics", "--max-strings", "--max-text-chars", "--max-memory-mb", "--max-depth" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (i + 1 == args.Length || !known.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Unknown, duplicate, or incomplete argument: {args[i]}");
        string Required(string key) => values.TryGetValue(key, out var value) ? value : throw new ArgumentException($"Missing {key}");
        var plugin = Path.GetFullPath(Required("--plugin"));
        var strings = Path.GetFullPath(Required("--strings-folder"));
        var bsa = Path.GetFullPath(values.GetValueOrDefault("--bsa-folder", strings));
        var output = Path.GetFullPath(Required("--output"));
        var manifest = values.TryGetValue("--manifest", out var manifestPath) ? Path.GetFullPath(manifestPath) : null;
        if (!File.Exists(plugin) || !new[] { ".esp", ".esm", ".esl" }.Contains(Path.GetExtension(plugin), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Plugin must be an existing ESP, ESM, or ESL.");
        if (!Directory.Exists(strings) || !Directory.Exists(bsa))
            throw new ArgumentException("Explicit strings and archive folders must already exist.");
        if (!Directory.Exists(Path.GetDirectoryName(output)) || Path.GetExtension(output) != ".json" || File.Exists(output))
            throw new ArgumentException("Output must be a new .json file in an existing folder.");
        if (manifest is not null && !File.Exists(manifest)) throw new ArgumentException("Manifest does not exist.");
        if (!Enum.TryParse<Language>(values.GetValueOrDefault("--language", "English"), true, out var language) || !Enum.IsDefined(language))
            throw new ArgumentException("Unknown language.");
        long Limit(string name, long fallback, long maximum)
        {
            if (!values.TryGetValue(name, out var raw)) return fallback;
            if (!long.TryParse(raw, out var limit) || limit < 1 || limit > maximum)
                throw new ArgumentException($"{name} must be between 1 and {maximum}.");
            return limit;
        }
        return new(plugin, strings, bsa, Required("--source-encoding"), Required("--tables-encoding"), Required("--metadata-encoding"), language, output, manifest,
            (int)Limit("--max-records", 0, int.MaxValue), Limit("--max-visited", 5_000_000, 100_000_000),
            (int)Limit("--max-diagnostics", 1_000, 10_000), (int)Limit("--max-strings", 500_000, 2_000_000),
            Limit("--max-text-chars", 64_000_000, 256_000_000), (int)Limit("--max-memory-mb", 1024, 4096),
            (int)Limit("--max-depth", 48, 96));
    }
}

internal sealed record RecordIdentity(string RecordType, string FormKey, uint RawFormId, string? EditorId, bool Deleted, uint RawFlags);
internal sealed record InventoryString(RecordIdentity Record, string PropertyPath, string Kind, string? Text, string RuntimeType,
    string ValueState = "resolved", uint? StringsKey = null);
internal sealed record InputEvidence(string Path, long Bytes, string Sha256);
internal sealed record InventoryDiagnostic(string Severity, string Code, string Path, string Detail);
internal sealed record DeclaredNumericSchemaExclusion(string Reason, string Source)
{
    public long Count { get; set; }
}
internal sealed record EnumIndexerEvidence(string[] Members, string Policy)
{
    public long Attempted { get; set; }
    public long Completed { get; set; }
}
internal sealed class InventoryReport
{
    public int SchemaVersion { get; } = 1;
    public string Reader { get; } = "Mutagen.Bethesda.Skyrim 0.53.1";
    public string SourceCommit { get; } = "bdbb6ff6faad3b4c3b0689e98e889ecd205068c8";
    public string Scope { get; } = "Independent major-record getter inventory; translated values and plain strings are distinct. Property paths are not binary subrecord identities. No claim of complete display-text classification.";
    public required InventoryOptions Options { get; init; }
    public string? ModKey { get; set; }
    public bool? Localized { get; set; }
    public string[] Masters { get; set; } = [];
    public List<InputEvidence> Inputs { get; } = [];
    public int RecordCount { get; set; }
    public Dictionary<string, int> RecordsByType { get; } = new(StringComparer.Ordinal);
    public List<InventoryString> Strings { get; } = [];
    public List<InventoryDiagnostic> Diagnostics { get; } = [];
    public Dictionary<string, int> LeafTypesWithoutProperties { get; } = new(StringComparer.Ordinal);
    public int NestedMajorRecordEdgesSkipped { get; set; }
    public int TranslatedStringCount { get; set; }
    public int NonEmptyTranslatedStringCount { get; set; }
    public int PlainStringCount { get; set; }
    public Dictionary<string, int> TranslatedValueStates => Strings.Where(item => item.Kind == "translated")
        .GroupBy(item => item.ValueState, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    public bool EnumerationReachedEnd { get; set; }
    public bool InputHashesRechecked { get; set; }
    public bool TraversalCompleted { get; set; }
    public object? Comparison { get; set; }
    public string? ComparisonSkippedReason { get; set; }
    public string? StopCode { get; set; }
    public string? StopDetail { get; set; }
    public long VisitedNodeCount { get; set; }
    public long RetainedTextChars { get; set; }
    public long PeakObservedWorkingSetBytes { get; set; }
    public long PeakObservedManagedBytes { get; set; }
    public Dictionary<string, long> PrunedNumericTypes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> PrunedNumericSequenceTypes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DeclaredNumericSchemaExclusion> DeclaredNumericSchemaExclusions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, EnumIndexerEvidence> EnumIndexerSchemas { get; } = new(StringComparer.Ordinal);
    public long KnownEnumAliasPropertiesSkipped { get; set; }

    internal void AddDiagnostic(InventoryDiagnostic diagnostic)
    {
        if (Diagnostics.Count >= Options.MaxDiagnostics)
            throw new InventoryLimitException("diagnostic_limit", $"Diagnostic limit {Options.MaxDiagnostics} reached; next failure was {diagnostic.Code} at {diagnostic.Path}.");
        Diagnostics.Add(diagnostic with { Detail = diagnostic.Detail.Length > 4096 ? diagnostic.Detail[..4096] + " [truncated]" : diagnostic.Detail });
    }

    internal void AddString(InventoryString value)
    {
        if (Strings.Count >= Options.MaxStrings)
            throw new InventoryLimitException("string_count_limit", $"String count limit {Options.MaxStrings} reached at {value.PropertyPath}.");
        var textChars = value.Text?.Length ?? 0;
        if (textChars > Options.MaxTextChars - RetainedTextChars)
            throw new InventoryLimitException("text_size_limit", $"Text character limit {Options.MaxTextChars} reached at {value.PropertyPath}.");
        RetainedTextChars += textChars;
        Strings.Add(value);
    }

    internal void MarkStopped(Exception error)
    {
        StopCode = error is InventoryLimitException limit ? limit.Code : "reader_failure";
        StopDetail = error.Message;
        if (Diagnostics.Count < Options.MaxDiagnostics)
            Diagnostics.Add(new("error", StopCode, Options.Plugin, error.ToString().Length > 4096 ? error.ToString()[..4096] : error.ToString()));
    }

    internal void CountPruned(Dictionary<string, long> counts, Type type)
    {
        var key = type.FullName ?? type.Name;
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}

internal static class InventoryComparison
{
    internal static object Compare(string manifestPath, List<InventoryString> inventory, InventoryBudget budget, string pluginSha256)
    {
        // Parse and hash the same immutable snapshot. The manifest file is never
        // read again, even if a concurrent process replaces its path afterward.
        var manifestBytes = File.ReadAllBytes(manifestPath);
        var manifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes));
        using var manifest = JsonDocument.Parse(manifestBytes);
        budget.Checkpoint("loaded comparison manifest", forceMemoryCheck: true);
        var manifestPluginSha256 = manifest.RootElement.GetProperty("Info").GetProperty("Sha256").GetString();
        if (!string.Equals(manifestPluginSha256, pluginSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Manifest input SHA256 {manifestPluginSha256 ?? "<missing>"} does not match inventoried plugin SHA256 {pluginSha256}.");
        var fields = manifest.RootElement.GetProperty("Fields").EnumerateArray().Select(field => new
        {
            Key = field.GetProperty("Key").GetString()!,
            RecordType = field.GetProperty("RecordType").GetString()!,
            RawFormId = field.GetProperty("FormId").GetUInt32(),
            Text = field.GetProperty("SourceText").GetString()!,
        }).ToArray();
        var translated = inventory.Where(item => item.Kind == "translated" && !item.Record.Deleted && !string.IsNullOrEmpty(item.Text)).ToArray();
        var plain = inventory.Where(item => item.Kind == "plain" && !item.Record.Deleted && !string.IsNullOrEmpty(item.Text))
            .GroupBy(item => (item.Record.RecordType, item.Record.RawFormId, item.Text!))
            .ToDictionary(group => group.Key, group => group.Select(item => item.PropertyPath).ToArray());
        var available = translated.GroupBy(item => (item.Record.RecordType, item.Record.RawFormId, item.Text!))
            .ToDictionary(group => group.Key, group => new Queue<InventoryString>(group));
        var matched = 0;
        var manifestOnly = new List<object>();
        foreach (var field in fields)
        {
            budget.Checkpoint("comparing manifest fields");
            var key = (field.RecordType, field.RawFormId, field.Text);
            if (available.TryGetValue(key, out var queue) && queue.TryDequeue(out _)) ++matched;
            else manifestOnly.Add(new { field.Key, field.RecordType, field.RawFormId, field.Text, PlainStringPaths = plain.GetValueOrDefault(key) ?? [] });
        }
        var inventoryOnly = available.Values.SelectMany(queue => queue).ToArray();
        return new
        {
            Method = "Exact ordinal (record signature, file-local raw FormID, text) multiset; no newline normalization; no subtype/occurrence identity assertion",
            ManifestPath = Path.GetFullPath(manifestPath),
            ManifestSha256 = manifestSha256,
            PluginSha256 = pluginSha256,
            ManifestPluginSha256Matches = true,
            ManifestCount = fields.Length,
            NonEmptyTranslatedInventoryCount = translated.Length,
            DeletedTranslatedInventoryCount = inventory.Count(item => item.Kind == "translated" && item.Record.Deleted),
            EmptyNonDeletedTranslatedInventoryCount = inventory.Count(item => item.Kind == "translated" && !item.Record.Deleted && string.IsNullOrEmpty(item.Text)),
            MatchedCount = matched,
            ManifestOnlyCount = manifestOnly.Count,
            InventoryOnlyCount = inventoryOnly.Length,
            ManifestOnly = manifestOnly,
            InventoryOnly = inventoryOnly,
            PlainStringCount = plain.Values.Sum(paths => paths.Length),
            Interpretation = "Unmatched getter paths and plain-string matches require manual classification. Equal multisets do not prove binary field identity or semantic translatability.",
        };
    }
}
