using System.Collections.Generic;

namespace AccountaBall.Core.Storage;

/// Light key/value settings, the Windows analog of macOS `UserDefaults`. The
/// Platform layer backs this with ApplicationData/JSON; tests use the in-memory
/// implementation. `GetInt` returns 0 for an absent key (mirrors
/// `UserDefaults.integer(forKey:)`), which `AppState.LoadDriftLimit` treats as "unset".
public interface IKeyValueStore
{
    int GetInt(string key);
    void SetInt(string key, int value);
    byte[]? GetData(string key);
    void SetData(string key, byte[] value);
    void Remove(string key);
}

/// Process-memory store for tests (and a safe default).
public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, byte[]> _data = new();
    private readonly Dictionary<string, int> _ints = new();

    public int GetInt(string key) => _ints.TryGetValue(key, out var v) ? v : 0;
    public void SetInt(string key, int value) => _ints[key] = value;
    public byte[]? GetData(string key) => _data.TryGetValue(key, out var v) ? v : null;
    public void SetData(string key, byte[] value) => _data[key] = value;

    public void Remove(string key)
    {
        _data.Remove(key);
        _ints.Remove(key);
    }
}
