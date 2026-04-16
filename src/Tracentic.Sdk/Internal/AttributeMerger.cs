namespace Tracentic.Sdk.Internal;

/// <summary>
/// Merges three layers of attributes into a single flat dictionary.
/// Priority (lowest → highest): global → scope → span.
/// On key collision the higher layer wins. The merge always produces
/// a new dictionary - no input is mutated.
///
/// Enforces <see cref="AttributeLimits"/> to prevent oversized payloads:
/// keys and string values are truncated, and the total attribute count
/// is capped. When the count cap is reached, lower-priority attributes
/// are the ones that get dropped (global first, then scope, then span).
/// </summary>
internal sealed class AttributeMerger
{
    private readonly TracenticGlobalContext _global;
    private readonly AttributeLimits _limits;

    public AttributeMerger(TracenticGlobalContext global, AttributeLimits limits)
    {
        _global = global;
        _limits = limits;
    }

    public IReadOnlyDictionary<string, object> Merge(
        TracenticScope? scope,
        IReadOnlyDictionary<string, object>? spanAttributes)
    {
        // Build the result in priority order (span → scope → global) so that
        // when MaxAttributeCount is hit, the lower-priority layers are the ones
        // dropped - never a span-level attribute.
        var result = new Dictionary<string, object>(_limits.MaxAttributeCount);

        if (spanAttributes is not null)
            AddLayer(result, spanAttributes);

        if (scope is not null && result.Count < _limits.MaxAttributeCount)
            AddLayer(result, scope.Attributes);

        if (result.Count < _limits.MaxAttributeCount)
            AddLayer(result, _global.GetAll());

        return result;
    }

    private void AddLayer(
        Dictionary<string, object> result,
        IEnumerable<KeyValuePair<string, object>> layer)
    {
        foreach (var (key, value) in layer)
        {
            var safeKey = key.Length > _limits.MaxKeyLength
                ? key[.._limits.MaxKeyLength]
                : key;

            // Higher-priority layer already wrote this key - skip; do not let a
            // lower-priority layer overwrite it.
            if (result.ContainsKey(safeKey)) continue;

            if (result.Count >= _limits.MaxAttributeCount) return;

            var safeValue = value is string s && s.Length > _limits.MaxStringValueLength
                ? s[.._limits.MaxStringValueLength]
                : value;

            result[safeKey] = safeValue;
        }
    }
}
