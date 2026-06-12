using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AccountaBall.Core.Storage;

namespace AccountaBall.Platform.Storage;

/// <see cref="IKeyValueStore"/> backed by a JSON file (the Windows analog of
/// UserDefaults). Unpackaged apps can't rely on <c>ApplicationData.Current</c>, so
/// settings live at <c>%LOCALAPPDATA%\AccountaBall\settings.json</c>. Ints and
/// byte-blobs (base64) share one document; every set persists immediately.
public sealed class FileKeyValueStore : IKeyValueStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _ints = new();
    private readonly Dictionary<string, string> _data = new();   // base64

    public FileKeyValueStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AccountaBall", "settings.json");
        Load();
    }

    public int GetInt(string key)
    {
        lock (_gate) return _ints.TryGetValue(key, out var v) ? v : 0;
    }

    public void SetInt(string key, int value)
    {
        lock (_gate) { _ints[key] = value; Save(); }
    }

    public byte[]? GetData(string key)
    {
        lock (_gate) return _data.TryGetValue(key, out var v) ? Convert.FromBase64String(v) : null;
    }

    public void SetData(string key, byte[] value)
    {
        lock (_gate) { _data[key] = Convert.ToBase64String(value); Save(); }
    }

    public void Remove(string key)
    {
        lock (_gate) { _ints.Remove(key); _data.Remove(key); Save(); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var doc = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(_path));
            if (doc is null) return;
            foreach (var kv in doc.Ints) _ints[kv.Key] = kv.Value;
            foreach (var kv in doc.Data) _data[kv.Key] = kv.Value;
        }
        catch
        {
            // A corrupt settings file must not block startup; start empty.
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var doc = new Persisted { Ints = _ints, Data = _data };
        File.WriteAllText(_path, JsonSerializer.Serialize(doc));
    }

    private sealed class Persisted
    {
        public Dictionary<string, int> Ints { get; set; } = new();
        public Dictionary<string, string> Data { get; set; } = new();
    }
}
