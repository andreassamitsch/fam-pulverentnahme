using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class InventoryService
{
    private readonly string _connectionString;
    private readonly OxaionOptions _oxaion;

    internal const CommandBehavior ReaderBehavior = CommandBehavior.Default;

    public InventoryService(IConfiguration configuration, IOptions<OxaionOptions> oxaion)
    {
        _connectionString = configuration["OxaionSql:ConnectionString"] ?? "";
        _oxaion = oxaion.Value;
    }

    internal const string QueryText = """
SELECT
 X.Firma,
 X.Lagerort,
 X.Lagerplatz,
 CAST(X.Artikel as nvarchar(12)) AS Artikel,
 CAST(X.Artikelbezeichnung as nvarchar(20)) AS Artikelbezeichnung,
 TRIM(X.Charge) AS Charge,
 X.Bestand,
 X.Einheit,
 X.ChargeDatum
FROM
(
 /* Reale Lagerplatzbestaende */
 SELECT
  B.LPFIRM AS Firma,
  B.LPLAGO AS Lagerort,
  B.LPLAPL AS Lagerplatz,
  B.LPIDNR AS Artikel,
  P.POCHBZ AS Artikelbezeichnung,
  B.LPPONR AS Charge,
  B.LPLABE AS Bestand,
  A.TLMEK1 AS Einheit,
  P.POPRDT AS ChargeDatum
 FROM OXAION.LLPWEP AS B
 LEFT JOIN OXAION.UTLSTP AS A
  ON A.TLFIRM = B.LPFIRM
 AND A.TLIDNR = B.LPIDNR
 LEFT JOIN OXAION.UPOSTP AS P
  ON P.POFIRM = B.LPFIRM
 AND P.POIDNR = B.LPIDNR
 AND P.POPONR = B.LPPONR
 WHERE B.LPFIRM = @firm
 AND B.LPPONR <> N''
 AND B.LPIDNR LIKE N'RP.%'
 AND B.LPLABE <> 0

 UNION ALL

 /* Lagerortbestaende ohne zusaetzliche Lagerplatzzeile */
 SELECT
  LA.LAFIRM AS Firma,
  LA.LALAGO AS Lagerort,
  N'' AS Lagerplatz,
  LA.LAIDNR AS Artikel,
  P.POCHBZ AS Artikelbezeichnung,
  LA.LAPONR AS Charge,
  LA.LALABE AS Bestand,
  A.TLMEK1 AS Einheit,
  P.POPRDT AS ChargeDatum
 FROM OXAION.LLAWEP AS LA
 LEFT JOIN OXAION.UTLSTP AS A
  ON A.TLFIRM = LA.LAFIRM
 AND A.TLIDNR = LA.LAIDNR
 LEFT JOIN OXAION.UPOSTP AS P
  ON P.POFIRM = LA.LAFIRM
 AND P.POIDNR = LA.LAIDNR
 AND P.POPONR = LA.LAPONR
 WHERE LA.LAFIRM = @firm
 AND LA.LAPONR <> N''
 AND LA.LAIDNR LIKE N'RP.%'
 AND LA.LALABE <> 0
 AND LA.LAGRKZ <> N'J'
) AS X
ORDER BY
 X.Firma,
 X.Lagerort,
 X.Lagerplatz,
 X.Artikel,
 X.Charge;
""";

    public async Task<IReadOnlyList<InventoryPosition>> ReadRpStockAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException(
                "Oxaion SQL-Verbindung ist nicht konfiguriert. OxaionSql__ConnectionString muss gesetzt sein.");
        if (string.IsNullOrWhiteSpace(_oxaion.Firm))
            throw new InvalidOperationException("Oxaion-Firma ist nicht konfiguriert.");

        var result = new List<InventoryPosition>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = QueryText;
        command.CommandType = CommandType.Text;
        command.Parameters.Add("@firm", SqlDbType.NVarChar, 3).Value = _oxaion.Firm;

        // Do not use SequentialAccess here. The mapper addresses columns by name and therefore
        // legitimately reads them in a different order than the SELECT projection. SequentialAccess
        // caused the Android error "Spaltenordinalzahl 1 ... nur 3 oder größer".
        await using var reader = await command.ExecuteReaderAsync(ReaderBehavior, ct);
        while (await reader.ReadAsync(ct))
        {
            var article = Text(reader, "Artikel");
            var warehouse = Text(reader, "Lagerort");
            var batch = Text(reader, "Charge");
            if (!article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(warehouse)
                || string.IsNullOrWhiteSpace(batch))
                continue;

            var quantity = Decimal(reader, "Bestand");
            if (quantity == 0m) continue;

            result.Add(new InventoryPosition(
                article,
                Text(reader, "Artikelbezeichnung"),
                warehouse,
                warehouse,
                Text(reader, "Lagerplatz"),
                batch,
                quantity,
                Text(reader, "Einheit"),
                quantity < 0m));
        }

        return result;
    }

    private static string Text(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? "" : (reader.GetValue(ordinal)?.ToString() ?? "").Trim();
    }

    private static decimal Decimal(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) return 0m;
        return Convert.ToDecimal(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
    }
}
