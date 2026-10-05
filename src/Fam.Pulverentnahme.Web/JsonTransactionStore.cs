using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class JsonTransactionStore
{
    private readonly string _baseDirectory;
    private readonly RuntimeConfigurationService _runtime;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public JsonTransactionStore(
        IWebHostEnvironment env,
        IOptions<PrototypeOptions> options,
        RuntimeConfigurationService runtime)
    {
        var configured = options.Value.TransactionDirectory;
        _baseDirectory = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
        _runtime = runtime;
        Directory.CreateDirectory(_baseDirectory);
        MigrateLegacyFilesToStaging(_baseDirectory);
    }

    public SemaphoreSlim GetLock(string clientOperationId) =>
        _locks.GetOrAdd(_runtime.EnvironmentName + ":" + clientOperationId, _ => new SemaphoreSlim(1, 1));

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
        return Path.Combine(EnvironmentDirectory(), safe + ".json");
    }

    private string EnvironmentDirectory()
    {
        var directory = Path.Combine(_baseDirectory, _runtime.EnvironmentName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void MigrateLegacyFilesToStaging(string baseDirectory)
    {
        var staging = Path.Combine(baseDirectory, RuntimeEnvironmentNames.Staging);
        Directory.CreateDirectory(staging);
        foreach (var file in Directory.EnumerateFiles(baseDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(staging, Path.GetFileName(file));
            if (!File.Exists(target)) File.Move(file, target);
        }
    }
}
