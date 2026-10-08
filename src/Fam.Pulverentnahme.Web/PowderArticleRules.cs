namespace Fam.Pulverentnahme.Web;

/// <summary>
/// Artikelkreise fuer FAM-Pulver:
/// RP.* = reguläres Pulver, PB.* = vom Kunden beigestelltes Pulver.
/// Die Artikelkreise sind getrennte Oxaion-Artikel; gleiche Bezeichnung
/// bedeutet niemals gleiche Buchungsfreigabe.
/// </summary>
public static class PowderArticleRules
{
    public static bool IsPowderArticle(string? article)
    {
        var value = (article ?? "").Trim();
        return value.StartsWith("RP.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("PB.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCustomerSupplied(string? article) =>
        (article ?? "").Trim().StartsWith("PB.", StringComparison.OrdinalIgnoreCase);
}
