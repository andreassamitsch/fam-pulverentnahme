using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record TargetWarehouseOption(string Warehouse, bool BinManaged);
public sealed record TargetStorageBinOption(string StorageBin);

/// <summary>
/// Read-only lookup used only to help the operator select a real Oxaion target warehouse/bin.
/// The returned keys are not a booking authorization: LF booking still validates the selected
/// warehouse and bin through the confirmed Oxaion F4 lists immediately before the write.
/// </summary>
public sealed class TargetLocationLookupService
{
    private readonly string _connectionString;
    private readonly OxaionOptions _oxaion;

    internal const string WarehouseQueryText = """
SELECT DISTINCT TOP (50)
    TRIM(L.LGLAGO) AS Warehouse,
    TRIM(L.LGKLPL) AS BinManaged
FROM OXAION.ULGSTP AS L
WHERE L.LGFIRM = @firm
  AND L.LGBFRM = @firm
  AND L.LGLOKZ = N''
  AND L.LGLAGO <> N''
  AND L.LGKLPL = N'J'
  AND (@excludeWarehouse = N'' OR UPPER(L.LGLAGO) <> UPPER(@excludeWarehouse))
  AND (@prefix = N'' OR UPPER(L.LGLAGO) LIKE UPPER(@prefix) ESCAPE N'\')
ORDER BY Warehouse;
""";

    internal const string StorageBinQueryText = """
SELECT DISTINCT TOP (100)
    TRIM(LP.LPLAPL) AS StorageBin
FROM OXAION.LLPLAP AS LP
WHERE LP.LPFIRM = @firm
  AND UPPER(LP.LPLAGO) = UPPER(@warehouse)
  AND LP.LPLAPL <> N''
  AND (@prefix = N'' OR UPPER(LP.LPLAPL) LIKE UPPER(@prefix) ESCAPE N'\')
ORDER BY StorageBin;
""";

    public TargetLocationLookupService(IConfiguration configuration, IOptions<OxaionOptions> oxaion)
    {
        _connectionString = configuration["OxaionSql:ConnectionString"] ?? "";
        _oxaion = oxaion.Value;
    }

    public async Task<IReadOnlyList<TargetWarehouseOption>> SearchWarehousesAsync(
        string? query,
        string? excludeWarehouse,
        CancellationToken ct)
    {
        EnsureConfigured();
        var result = new List<TargetWarehouseOption>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = WarehouseQueryText;
        command.CommandType = CommandType.Text;
        command.Parameters.Add("@firm", SqlDbType.NVarChar, 3).Value = _oxaion.Firm;
        command.Parameters.Add("@excludeWarehouse", SqlDbType.NVarChar, 20).Value = Normalize(excludeWarehouse, 20);
        command.Parameters.Add("@prefix", SqlDbType.NVarChar, 80).Value = Prefix(query, 40);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, ct);
        while (await reader.ReadAsync(ct))
        {
            var warehouse = reader.IsDBNull(0) ? "" : (reader.GetString(0) ?? "").Trim();
            var binManaged = !reader.IsDBNull(1)
                && string.Equals((reader.GetString(1) ?? "").Trim(), "J", StringComparison.OrdinalIgnoreCase);
            if (warehouse.Length > 0) result.Add(new TargetWarehouseOption(warehouse, binManaged));
        }
        return result;
    }

    public async Task<IReadOnlyList<TargetStorageBinOption>> SearchStorageBinsAsync(
        string warehouse,
        string? query,
        CancellationToken ct)
    {
        EnsureConfigured();
        warehouse = Normalize(warehouse, 20);
        if (warehouse.Length == 0) throw new ArgumentException("Lagerort ist erforderlich.");

        var result = new List<TargetStorageBinOption>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = StorageBinQueryText;
        command.CommandType = CommandType.Text;
        command.Parameters.Add("@firm", SqlDbType.NVarChar, 3).Value = _oxaion.Firm;
        command.Parameters.Add("@warehouse", SqlDbType.NVarChar, 20).Value = warehouse;
        command.Parameters.Add("@prefix", SqlDbType.NVarChar, 80).Value = Prefix(query, 40);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, ct);
        while (await reader.ReadAsync(ct))
        {
            var storageBin = reader.IsDBNull(0) ? "" : (reader.GetString(0) ?? "").Trim();
            if (storageBin.Length > 0) result.Add(new TargetStorageBinOption(storageBin));
        }
        return result;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException(
                "Oxaion SQL-Verbindung ist nicht konfiguriert. OxaionSql__ConnectionString muss gesetzt sein.");
        if (string.IsNullOrWhiteSpace(_oxaion.Firm))
            throw new InvalidOperationException("Oxaion-Firma ist nicht konfiguriert.");
    }

    internal static string Prefix(string? value, int maxLength)
    {
        var normalized = Normalize(value, maxLength);
        if (normalized.Length == 0) return "";
        return EscapeLike(normalized) + "%";
    }

    private static string Normalize(string? value, int maxLength)
    {
        var normalized = (value ?? "").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal);
}
