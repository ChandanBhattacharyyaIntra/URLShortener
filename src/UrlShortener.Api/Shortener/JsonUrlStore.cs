using System.Collections.Concurrent;
using System.Text.Json;

namespace UrlShortener.Api.Shortener;

public sealed class JsonUrlStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ConcurrentDictionary<string, ShortUrl> _urls = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<ClickEvent> _clicks = [];
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly string _path;

    public JsonUrlStore(string path) => _path = path;

    public async Task LoadAsync()
    {
        if (!File.Exists(_path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? "data");
            return;
        }

        await using var stream = File.OpenRead(_path);
        var snap = await JsonSerializer.DeserializeAsync<StoreSnapshot>(stream) ?? new StoreSnapshot();
        foreach (var url in snap.Urls)
            _urls[url.Code] = url;
        foreach (var click in snap.Clicks)
            _clicks.Add(click);
    }

    public ShortUrl? Get(string code) => _urls.TryGetValue(code, out var url) ? url : null;

    public IReadOnlyList<ShortUrl> List() =>
        _urls.Values.OrderByDescending(u => u.CreatedAt).ToList();

    public void Put(ShortUrl url)
    {
        _urls[url.Code] = url;
        QueuePersist();
    }

    public void AddClick(ClickEvent click)
    {
        _clicks.Add(click);
        QueuePersist();
    }

    public IReadOnlyList<ClickEvent> ClicksFor(string code) =>
        _clicks.Where(c => c.Code == code).OrderBy(c => c.Timestamp).ToList();

    public StoreSnapshot Snapshot() => new()
    {
        Urls = List().ToList(),
        Clicks = _clicks.ToList()
    };

    private void QueuePersist() => _ = PersistAsync();

    private async Task PersistAsync()
    {
        await _write.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? "data");
            var tmp = _path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, Snapshot(), JsonOptions);
            }
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _write.Release();
        }
    }
}
