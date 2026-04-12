namespace Tracentic.Sdk.Internal;

/// <summary>
/// Merges three layers of attributes into a single flat dictionary.
/// Priority (lowest → highest): global → scope → span.
/// On key collision the higher layer wins. The merge always produces
/// a new dictionary — no input is mutated.
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
        // Layer 1 — global (lowest priority)
        var result = new Dictionary<string, object>(_global.GetAll());

        // Layer 2 — scope attributes
        if (scope is not null)
            foreach (var (k, v) in scope.Attributes)
                result[k] = v;

        // Layer 3 — span-level (highest priority)
        if (spanAttributes is not null)
            foreach (var (k, v) in spanAttributes)
                result[k] = v;

        return Enforce(result);
    }

    private Dictionary<string, object> Enforce(Dictionary<string, object> attrs)
    {
        var result = new Dictionary<string, object>(attrs.Count);

        foreach (var (key, value) in attrs)
        {
            var safeKey = key.Length > _limits.MaxKeyLength
                ? key[.._limits.MaxKeyLength]
                : key;

            var safeValue = value is string s && s.Length > _limits.MaxStringValueLength
                ? s[.._limits.MaxStringValueLength]
                : value;

            result[safeKey] = safeValue;

            if (result.Count >= _limits.MaxAttributeCount)
                break;
        }

        return result;
    }
}
