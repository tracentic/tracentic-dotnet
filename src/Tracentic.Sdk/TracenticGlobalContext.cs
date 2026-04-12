namespace Tracentic;

/// <summary>
/// Thread-safe store for global attributes applied to every span.
/// Accessible via DI or via the static Current accessor.
/// </summary>
public class TracenticGlobalContext
{
    private static TracenticGlobalContext? _current;

    /// <summary>Singleton instance. Same object as the DI registration.</summary>
    public static TracenticGlobalContext Current
        => _current ?? throw new InvalidOperationException(
            "TracenticGlobalContext has not been initialized. " +
            "Call services.AddTracentic() first.");

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<string, object> _attributes = new();

    /// <summary>Set a global attribute.</summary>
    public void Set(string key, object value)
    {
        _lock.EnterWriteLock();
        try
        {
            _attributes[key] = value;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>Remove a global attribute.</summary>
    public void Remove(string key)
    {
        _lock.EnterWriteLock();
        try
        {
            _attributes.Remove(key);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>Snapshot of all current global attributes.</summary>
    public IReadOnlyDictionary<string, object> GetAll()
    {
        _lock.EnterReadLock();
        try
        {
            return new Dictionary<string, object>(_attributes);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    internal static void SetCurrent(TracenticGlobalContext instance)
        => _current = instance;

    internal static void ResetCurrent()
        => _current = null;
}
