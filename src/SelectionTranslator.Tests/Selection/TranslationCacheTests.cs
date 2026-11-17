using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.Tests.Selection;

public class TranslationCacheTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}.json");

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Set_then_get_returns_value()
    {
        using var cache = new TranslationCache(_path);
        cache.Set("k", "v");
        Assert.True(cache.TryGet("k", out var value));
        Assert.Equal("v", value);
    }

    [Fact]
    public void Missing_key_returns_false()
    {
        using var cache = new TranslationCache(_path);
        Assert.False(cache.TryGet("missing", out _));
    }

    [Fact]
    public void Evicts_least_recently_used()
    {
        using var cache = new TranslationCache(_path, capacity: 2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.TryGet("a", out _);
        cache.Set("c", "3");

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Expires_after_ttl()
    {
        var time = new FakeTime();
        using var cache = new TranslationCache(_path, ttl: TimeSpan.FromDays(30), timeProvider: time);
        cache.Set("k", "v");
        time.Now = time.Now.AddDays(31);
        Assert.False(cache.TryGet("k", out _));
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        using (var cache = new TranslationCache(_path))
        {
            cache.Set("k", "v");
            cache.Save();
        }

        using var reloaded = new TranslationCache(_path);
        reloaded.Load();
        Assert.True(reloaded.TryGet("k", out var value));
        Assert.Equal("v", value);
    }

    [Fact]
    public void Same_input_produces_same_key()
    {
        var a = CacheKey.For("glm", "glm-4-flash", "简体中文", "hello");
        var b = CacheKey.For("glm", "glm-4-flash", "简体中文", "hello");
        var c = CacheKey.For("glm", "glm-4-flash", "简体中文", "hello!");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}