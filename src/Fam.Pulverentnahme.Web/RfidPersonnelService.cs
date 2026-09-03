using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record RfidPersonnelResult(
    string SerialNumber,
    string Rfid,
    string PersonnelNo,
    string FullName,
    string SyncosName,
    string SyncosDescription);

public sealed class RfidPersonnelService
{
    private readonly SyncosOptions _options;
    private readonly PersonnelService _personnel;

    public RfidPersonnelService(IOptions<SyncosOptions> options, PersonnelService personnel)
    {
        _options = options.Value;
        _personnel = personnel;
    }

    public async Task<RfidPersonnelResult?> ResolveAsync(string serialNumber, CancellationToken ct)
    {
        var rfid = NormalizeSerialNumber(serialNumber);
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("Syncos SQL-Verbindung ist nicht konfiguriert. Syncos__ConnectionString muss gesetzt sein.");

        var rows = new List<(string ObjectKey, string Name, string Description)>();
        await using (var connection = new SqlConnection(_options.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (2)
                    t0.ObjectKey,
                    t0.Name,
                    t0.Description
                FROM syncos_stg_102.ITSDEV.ITSUSER t0
                WHERE t0.ClassID = 47
                  AND t0.IsEnabled = -1
                  AND t0.IsVisible = -1
                  AND t0.RFID = @rfid
                ORDER BY t0.ObjectKey
                """;
            command.Parameters.Add("@rfid", SqlDbType.VarChar, 64).Value = rfid;

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    reader["ObjectKey"]?.ToString()?.Trim() ?? "",
                    reader["Name"]?.ToString()?.Trim() ?? "",
                    reader["Description"]?.ToString()?.Trim() ?? ""));
            }
        }

        if (rows.Count == 0) return null;
        if (rows.Count != 1)
            throw new InvalidOperationException($"RFID {rfid} ist in Syncos nicht eindeutig einer aktiven sichtbaren Person zugeordnet.");

        var row = rows[0];
        var personnelNo = NormalizeObjectKey(row.ObjectKey);
        var employee = await _personnel.ReadExactAsync(personnelNo, ct);
        if (employee is null)
            throw new InvalidOperationException($"RFID {rfid} verweist in Syncos auf Personalnummer {personnelNo}, diese konnte aber nicht eindeutig in oxaion bestätigt werden.");

        return new RfidPersonnelResult(
            serialNumber.Trim(),
            rfid,
            employee.PersonnelNo,
            employee.FullName,
            row.Name,
            row.Description);
    }

    internal static string NormalizeObjectKey(string objectKey)
    {
        var value = (objectKey ?? "").Trim();
        if (value.Length == 0 || !value.All(char.IsDigit))
            throw new InvalidOperationException("Syncos ObjectKey ist keine gültige numerische Personalnummer.");
        return PersonnelService.NormalizeInput(value);
    }

    internal static string NormalizeSerialNumber(string serialNumber)
    {
        var value = (serialNumber ?? "").Trim();
        if (value.Length == 0)
            throw new ArgumentException("Der NFC-Chip hat keine auslesbare Seriennummer.", nameof(serialNumber));

        // Syncos stores RFID as an alphanumeric identifier. Never interpret it as a decimal or hex number.
        // Web NFC/readers may insert separators between UID groups; remove separators only and preserve the identifier.
        var normalized = new string(value
            .Where(c => c != ':' && c != '-' && !char.IsWhiteSpace(c))
            .ToArray())
            .ToUpperInvariant();

        if (normalized.Length == 0 || !normalized.All(char.IsLetterOrDigit))
            throw new ArgumentException("NFC-Seriennummer enthält nicht unterstützte Zeichen.", nameof(serialNumber));

        return normalized;
    }
}
