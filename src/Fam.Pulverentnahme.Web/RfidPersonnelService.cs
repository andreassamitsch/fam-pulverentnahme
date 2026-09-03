using System.Data;
using System.Globalization;
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
        var rfid = SerialNumberToRfid(serialNumber);
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

    internal static string SerialNumberToRfid(string serialNumber)
    {
        var value = (serialNumber ?? "").Trim();
        if (value.Length == 0)
            throw new ArgumentException("Der NFC-Chip hat keine auslesbare Seriennummer.", nameof(serialNumber));

        // Web NFC exposes the tag serial/UID as hexadecimal bytes, normally separated by ':'.
        // For diagnostics/API tests we additionally accept an already-decimal RFID value.
        if (value.All(char.IsDigit) && !value.Contains(':') && !value.Contains('-') && !value.Contains(' '))
        {
            if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var direct))
                throw new ArgumentException("NFC-Seriennummer ist ungültig.", nameof(serialNumber));
            return direct.ToString(CultureInfo.InvariantCulture);
        }

        var hex = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length == 0 || hex.Length % 2 != 0 || hex.Length > 16)
            throw new ArgumentException("NFC-Seriennummer hat kein unterstütztes Hex-Format.", nameof(serialNumber));
        if (!ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var numeric))
            throw new ArgumentException("NFC-Seriennummer konnte nicht in RFID umgerechnet werden.", nameof(serialNumber));

        return numeric.ToString(CultureInfo.InvariantCulture);
    }
}
