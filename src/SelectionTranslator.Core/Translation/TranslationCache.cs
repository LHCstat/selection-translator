using System.Text.Json;

namespace SelectionTranslator.Core.Translation;

public sealed class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public CacheEntry()
    {
    }

    public CacheEntry(string key, string value, DateTimeOffset createdAt)
    {
        Key = key;
        Value = value;
        CreatedAt = createdAt;
    }
}

public sealed class TranslationCache : IDisposable
{
    private readonly int _capacity;
    private readonly string _path;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();

    public TranslationCache(
        string path,
        int capacity = 200,
        TimeSpan? ttl = null,
        TimeProvider? timeProvider = null)
    {
        _path = path;
        _capacity = capacity;
        _ttl = ttl ?? TimeSpan.FromDays(30);
        _time = timeProvider ?? TimeProvider.System;
    }

    public bool TryGet(string key, out string value)
    {
        lock (_lock)
        {
            value = string.Empty;
            if (!_map.TryGetValue(key, out var node))
                return false;

            if (IsExpired(node.Value))
            {
                _lru.Remove(node);
                _map.Remove(key);
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(string key, string value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _map.Remove(key);
            }

            var node = _lru.AddFirst(new CacheEntry(key, value, _time.GetUtcNow()));
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _lru.Last;
                if (last is null) break;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    public void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<CacheEntry>>(File.ReadAllText(_path));
            if (entries is null)
                return;

            lock (_lock)
            {
                _map.Clear();
                _lru.Clear();

                foreach (var entry in entries.OrderByDescending(e => e.CreatedAt))
                {
                    if (IsExpired(entry)) continue;
                    var node = _lru.AddFirst(entry);
                    _map[entry.Key] = node;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 缓存损坏不影响主流程，直接当作空缓存。
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _lru.Clear();
        }

        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    public void Save()
    {
        try
        {
            List<CacheEntry> snapshot;
            lock (_lock)
            {
                snapshot = _lru.ToList();
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
    }

    private bool IsExpired(CacheEntry entry) => _time.GetUtcNow() - entry.CreatedAt > _ttl;

    public void Dispose() => Save();
}