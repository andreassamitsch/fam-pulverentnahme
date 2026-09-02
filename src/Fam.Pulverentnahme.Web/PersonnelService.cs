using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record PersonnelOption(string PersonnelNo, string Code, string FullName);

internal sealed record PersonnelSearchSeed(Dictionary<string, string> Fields);

/// <summary>
/// Read-only personnel lookup reconstructed from the captured Oxaion JET flow of 2026-09-02.
/// Confirmed sequence:
/// MN10209J *CHKCMD (MA) -> US14090J *LOAD/*GETCFTIT/*FIRSTLIST/*SEARCH ->
/// US14000J *LOAD/*READ.
/// The operator enters the personnel number without leading zeroes. Oxaion returns PEPENU padded
/// to 10 digits. PESAKZ is the personnel short code and PENLAE is the full name.
/// </summary>
public sealed class PersonnelService
{
    private const int MaxResults = 10;
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;

    public PersonnelService(OxaionClient oxaion, IOptions<OxaionOptions> options)
    {
        _oxaion = oxaion;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<PersonnelOption>> SearchAsync(string query, CancellationToken ct)
    {
        query = NormalizeInput(query);
        if (string.IsNullOrWhiteSpace(query)) return [];

        await using var session = await _oxaion.ConnectAsync(ct);

        var command = await session.CallAsync("MN10209J", "*CHKCMD", Dict(
            ("CHKCMD", "MA"),
            ("_father_", "CMDLINE")), ct);
        OxaionSession.AssertNoFcod(command);
        var ssid = Get(command.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid))
            throw new InvalidOperationException("MN10209J *CHKCMD did not return an SSID for personnel lookup.");

        var listContext = Dict(("SSID", ssid), ("NOHWPgm", "US14090"));
        OxaionSession.AssertNoFcod(await session.CallAsync("US14090J", "*LOAD", listContext, ct));
        OxaionSession.AssertNoFcod(await session.CallAsync("US14090J", "*GETCFTIT", listContext, ct));
        OxaionSession.AssertNoFcod(await session.CallAsync("US14090J", "*FIRSTLIST", Dict(("SSID", ssid)), ct));

        var search = await session.CallAsync("US14090J", "*SEARCH", Dict(
            ("SSID", ssid),
            ("SEARCH", query),
            ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(search);
        var seeds = ParseSearchSeeds(search.Xml).Take(MaxResults).ToList();
        if (seeds.Count == 0) return [];

        OxaionSession.AssertNoFcod(await session.CallAsync("US14000J", "*LOAD", Dict(
            ("NOHWPgm", "US14000"),
            ("NoHints", "")), ct));

        var result = new List<PersonnelOption>();
        foreach (var seed in seeds)
        {
            var read = await session.CallAsync("US14000J", "*READ", seed.Fields, ct);
            OxaionSession.AssertNoFcod(read);
            var option = ParsePersonnel(read.Xml);
            if (option is null) continue;
            if (!result.Any(x => string.Equals(x.PersonnelNo, option.PersonnelNo, StringComparison.Ordinal)))
                result.Add(option);
        }

        return result;
    }

    public async Task<PersonnelOption?> ReadExactAsync(string personnelNo, CancellationToken ct)
    {
        var normalized = NormalizeInput(personnelNo);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        var matches = (await SearchAsync(normalized, ct))
            .Where(x => string.Equals(x.PersonnelNo, normalized, StringComparison.Ordinal))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    internal static IReadOnlyList<PersonnelSearchSeed> ParseSearchSeeds(XDocument xml)
    {
        var result = new List<PersonnelSearchSeed>();
        foreach (var tree in xml.Descendants("TREE"))
        {
            var key = tree.Element("KEY");
            var record = tree.Element("RECORD");
            if (key is null || record is null) continue;
            var penu = key.Element("PEPENU")?.Value.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(penu)) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var child in record.Elements()) fields[child.Name.LocalName] = child.Value.Trim();
            foreach (var child in key.Elements()) fields[child.Name.LocalName] = child.Value.Trim();
            fields["KEYTYPE"] = "UPERS";
            fields["PEPENU"] = penu;
            result.Add(new PersonnelSearchSeed(fields));
        }
        return result;
    }

    internal static PersonnelOption? ParsePersonnel(XDocument xml)
    {
        var dta = xml.Descendants("DTA").FirstOrDefault();
        if (dta is null) return null;
        string V(string name) => dta.Element(name)?.Value.Trim() ?? "";
        var no = NormalizeOxaionNumber(V("PEPENU"));
        var code = V("PESAKZ");
        var fullName = V("PENLAE");
        if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(fullName)) return null;
        return new PersonnelOption(no, code, fullName);
    }

    internal static string NormalizeInput(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) return "";
        if (!value.All(char.IsDigit)) throw new ArgumentException("Personalnummer darf nur Ziffern enthalten.");
        if (value.Length > 10) throw new ArgumentException("Personalnummer ist zu lang.");
        return NormalizeOxaionNumber(value);
    }

    private static string NormalizeOxaionNumber(string value)
    {
        var trimmed = (value ?? "").Trim().TrimStart('0');
        return trimmed.Length == 0 && !string.IsNullOrWhiteSpace(value) ? "0" : trimmed;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
}
