using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class JsonTransactionStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public JsonTransactionStore(IWebHostEnvironment env, IOptions<PrototypeOptions> options)
    {
        var configured = options.Value.TransactionDirectory;
        _directory = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(_directory);
    }

    public SemaphoreSlim GetLock(string clientOperationId) => _locks.GetOrAdd(clientOperationId, _ => new SemaphoreSlim(1, 1));

    public async Task<MixTransaction?> GetAsync(string clientOperationId, CancellationToken ct)
    {
        var path = PathFor(clientOperationId);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MixTransaction>(stream, _json, ct);
    }

    public async Task SaveAsync(MixTransaction tx, CancellationToken ct)
    {
        tx.UpdatedAt = DateTimeOffset.UtcNow;
        var path = PathFor(tx.ClientOperationId);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, tx, _json, ct);
        File.Move(temp, path, true);
    }

    private string PathFor(string id)
    {
        var safe = string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safe)) throw new ArgumentException("Invalid clientOperationId.");
        return Path.Combine(_directory, safe + ".json");
    }
}
