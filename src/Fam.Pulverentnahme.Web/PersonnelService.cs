using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed record PersonnelOption(string PersonnelNo, string FullName);

internal sealed record PersonnelSearchSeed(Dictionary<string, string> Fields);

/// <summary>
/// Read-only personnel lookup reconstructed from captured Oxaion JET flows.
/// AJAX suggestions still use US14090J *SEARCH as a candidate lookup, but candidates are accepted
/// only when their normalized PEPENU starts with the entered number. This prevents matches that
/// exist only in other free-search columns such as cost center.
///
/// Exact server-side validation before a booking uses the confirmed Oxaion personnel filter flow:
/// US14001R *GETFILTER -> US14001 *SAVCURSET -> US14001R *GETSLTV/*GETSLTATR/*CHKSLTV
/// with IPENU=<10-digit personnel number>, followed by US14090J *FIRSTLIST FROM_PGMN=MAINFILTER.
/// PEPENA is the full personnel name. PESAKZ and PENLAE are intentionally not used.
/// </summary>
public sealed class PersonnelService
{
    private const int MaxResults = 10;
    private readonly OxaionClient _oxaion;

    public PersonnelService(OxaionClient oxaion, IOptions<OxaionOptions> options)
    {
        _oxaion = oxaion;
        _ = options.Value;
    }

    public async Task<IReadOnlyList<PersonnelOption>> SearchAsync(string query, CancellationToken ct)
    {
        query = NormalizeInput(query);
        if (string.IsNullOrWhiteSpace(query)) return [];

        await using var session = await _oxaion.ConnectAsync(ct);
        var ssid = await OpenPersonnelListAsync(session, ct);

        var search = await session.CallAsync("US14090J", "*SEARCH", Dict(
            ("SSID", ssid),
            ("SEARCH", query),
            ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(search);

        // US14090J *SEARCH is a free search across the displayed columns. Never expose its result
        // directly. PEPENU is the authoritative field and the prefix check happens before the
        // result limit so cost-center/name matches cannot consume our ten returned slots.
        var seeds = ParseSearchSeeds(search.Xml)
            .Where(seed => MatchesPersonnelPrefix(Get(seed.Fields, "PEPENU"), query))
            .GroupBy(seed => NormalizeOxaionNumber(Get(seed.Fields, "PEPENU")), StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(MaxResults)
            .ToList();
        if (seeds.Count == 0) return [];

        var result = await ReadPersonnelOptionsAsync(session, seeds, ct);
        return result
            .Where(option => option.PersonnelNo.StartsWith(query, StringComparison.Ordinal))
            .Take(MaxResults)
            .ToList();
    }

    public async Task<PersonnelOption?> ReadExactAsync(string personnelNo, CancellationToken ct)
    {
        var normalized = NormalizeInput(personnelNo);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        await using var session = await _oxaion.ConnectAsync(ct);
        var ssid = await OpenPersonnelListAsync(session, ct);

        // This exact field filter is confirmed by the 2026-09-03 JET capture. Do not replace it
        // with the free search: the free search also matches values in unrelated displayed fields.
        var getFilter = await CallPersonnelValidationStepAsync(session, "US14001R", "*GETFILTER", Dict(
            ("SSID", ssid)), ct);
        OxaionSession.AssertNoFcod(getFilter);

        var saveCurrent = await CallPersonnelValidationStepAsync(session, "US14001", "*SAVCURSET", Dict(
            ("SSID", ssid)), ct);
        OxaionSession.AssertNoFcod(saveCurrent);

        var getSelection = await CallPersonnelValidationStepAsync(session, "US14001R", "*GETSLTV", Dict(
            ("SSID", ssid)), ct);
        OxaionSession.AssertNoFcod(getSelection);

        var attributeInput = new Dictionary<string, string>(getSelection.Dta, StringComparer.Ordinal)
        {
            ["SSID"] = ssid,
            ["mode"] = "merge"
        };
        var getAttributes = await CallPersonnelValidationStepAsync(session, "US14001R", "*GETSLTATR", attributeInput, ct);
        OxaionSession.AssertNoFcod(getAttributes);

        // GETSLTATR can enrich/replace selection state required by CHKSLTV. The previous
        // implementation discarded this response and sent only the older GETSLTV state.
        var selectionFields = BuildExactSelectionFields(
            getSelection.Dta,
            getAttributes.Dta,
            ssid,
            normalized);
        var checkSelection = await CallPersonnelValidationStepAsync(session, "US14001R", "*CHKSLTV", selectionFields, ct);
        OxaionSession.AssertNoFcod(checkSelection);

        var filtered = await CallPersonnelValidationStepAsync(session, "US14090J", "*FIRSTLIST", Dict(
            ("SSID", ssid),
            ("FROM_PGMN", "MAINFILTER"),
            ("mode", "replace-children")), ct);
        OxaionSession.AssertNoFcod(filtered);

        var exactSeeds = ParseSearchSeeds(filtered.Xml)
            .Where(seed => string.Equals(
                NormalizeOxaionNumber(Get(seed.Fields, "PEPENU")),
                normalized,
                StringComparison.Ordinal))
            .Take(2)
            .ToList();
        if (exactSeeds.Count != 1) return null;

        var matches = await ReadPersonnelOptionsAsync(session, exactSeeds, ct);
        return matches.Count == 1 && string.Equals(matches[0].PersonnelNo, normalized, StringComparison.Ordinal)
            ? matches[0]
            : null;
    }

    private static async Task<OxaionCallResult> CallPersonnelValidationStepAsync(
        OxaionSession session,
        string program,
        string action,
        IReadOnlyDictionary<string, string> dta,
        CancellationToken ct)
    {
        try
        {
            return await session.CallAsync(program, action, dta, ct);
        }
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message, "Oxaion response was not valid XML.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Oxaion response during personnel validation {program} {action} was not valid XML.",
                ex);
        }
    }

    private static async Task<string> OpenPersonnelListAsync(OxaionSession session, CancellationToken ct)
    {
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
        return ssid;
    }

    private static async Task<IReadOnlyList<PersonnelOption>> ReadPersonnelOptionsAsync(
        OxaionSession session,
        IReadOnlyList<PersonnelSearchSeed> seeds,
        CancellationToken ct)
    {
        if (seeds.Count == 0) return [];

        OxaionSession.AssertNoFcod(await session.CallAsync("US14000J", "*LOAD", Dict(
            ("NOHWPgm", "US14000"),
            ("NoHints", "")), ct));

        var result = new List<PersonnelOption>();
        foreach (var seed in seeds)
        {
            var expectedPersonnelNo = NormalizeOxaionNumber(Get(seed.Fields, "PEPENU"));
            if (string.IsNullOrWhiteSpace(expectedPersonnelNo)) continue;

            var read = await session.CallAsync("US14000J", "*READ", seed.Fields, ct);
            OxaionSession.AssertNoFcod(read);

            // A READ response can contain more than one DTA context. Never take the first DTA
            // blindly. Only the DTA whose PEPENU matches the requested personnel record may be
            // used, and the displayed/validated name comes exclusively from PEPENA.
            var option = ParsePersonnel(read.Xml, expectedPersonnelNo);
            if (option is null) continue;
            if (!result.Any(x => string.Equals(x.PersonnelNo, option.PersonnelNo, StringComparison.Ordinal)))
                result.Add(option);
        }

        return result;
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

    internal static PersonnelOption? ParsePersonnel(XDocument xml, string? expectedPersonnelNo = null)
    {
        var expected = string.IsNullOrWhiteSpace(expectedPersonnelNo)
            ? ""
            : NormalizeOxaionNumber(expectedPersonnelNo);

        var matches = new List<PersonnelOption>();
        foreach (var dta in xml.Descendants("DTA"))
        {
            string V(string name) => dta.Element(name)?.Value.Trim() ?? "";
            var no = NormalizeOxaionNumber(V("PEPENU"));
            var fullName = V("PEPENA");
            if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(fullName)) continue;
            if (expected.Length > 0 && !string.Equals(no, expected, StringComparison.Ordinal)) continue;

            var option = new PersonnelOption(no, fullName);
            if (!matches.Any(x =>
                    string.Equals(x.PersonnelNo, option.PersonnelNo, StringComparison.Ordinal) &&
                    string.Equals(x.FullName, option.FullName, StringComparison.Ordinal)))
                matches.Add(option);
        }

        // Ambiguous responses are rejected instead of guessing which name belongs to the person.
        return matches.Count == 1 ? matches[0] : null;
    }

    internal static bool MatchesPersonnelPrefix(string oxaionPersonnelNo, string normalizedPrefix)
    {
        if (string.IsNullOrWhiteSpace(normalizedPrefix)) return false;
        return NormalizeOxaionNumber(oxaionPersonnelNo)
            .StartsWith(normalizedPrefix, StringComparison.Ordinal);
    }

    internal static string ToOxaionPersonnelNumber(string personnelNo)
    {
        var normalized = NormalizeInput(personnelNo);
        return normalized.PadLeft(10, '0');
    }

    internal static Dictionary<string, string> BuildExactSelectionFields(
        IReadOnlyDictionary<string, string> selectionValues,
        IReadOnlyDictionary<string, string> selectionAttributes,
        string ssid,
        string personnelNo)
    {
        var result = new Dictionary<string, string>(selectionValues, StringComparer.Ordinal);
        foreach (var pair in selectionAttributes)
            result[pair.Key] = pair.Value ?? "";

        result["SSID"] = ssid;
        result["IPENU"] = ToOxaionPersonnelNumber(personnelNo);
        return result;
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
