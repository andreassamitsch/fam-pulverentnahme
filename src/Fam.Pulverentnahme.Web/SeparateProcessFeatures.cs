using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public interface ISeparatePersonnelRequest
{
    string ClientOperationId { get; }
    string PersonnelNo { get; }
    string PersonnelName { get; }
}

public sealed record TankOutRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string TankWarehouseText,
    string Article,
    string ArticleText,
    string Batch,
    decimal QuantityKg,
    string TargetWarehouse,
    string TargetStorageBin) : ISeparatePersonnelRequest;

public sealed record FillNewRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string TankWarehouseText,
    string Article,
    string ArticleText,
    string TargetBatch,
    DateOnly ProductionDate,
    DateOnly BookingDate,
    IReadOnlyList<AdditionalPowderSource> Sources) : ISeparatePersonnelRequest;

public sealed record FaConsumptionRequest(
    string ClientOperationId,
    string PersonnelNo,
    string PersonnelName,
    string TankWarehouse,
    string TankWarehouseText,
    string Article,
    string ArticleText,
    string TankBatch,
    decimal TankQuantityKg,
    string OrderNo,
    string PlannedMachineId,
    int MaterialPosition,
    decimal ExpectedRequiredKg,
    decimal ExpectedConsumedKg,
    int ExpectedMaterialStatus,
    decimal AdditionalConsumptionKg) : ISeparatePersonnelRequest;

public sealed record FaMaterialPositionResult(
    string OrderNo,
    int MaterialPosition,
    string Article,
    string ArticleText,
    decimal RequiredKg,
    decimal ConsumedKg,
    string Unit,
    int MaterialStatus,
    string MaterialStatusText,
    bool MkBookingAllowed,
    DateTimeOffset ReadAt);

public sealed record InventoryPosition(
    string Article,
    string ArticleText,
    string Warehouse,
    string WarehouseText,
    string StorageBin,
    string Batch,
    decimal QuantityKg,
    string Unit,
    bool NegativeStock);

public sealed class SeparateOperation
{
    public string Kind { get; set; } = "";
    public string ClientOperationId { get; set; } = "";
    public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = TransactionStatuses.Created;
    public string Stage { get; set; } = "CREATED";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RequestJson { get; set; } = "";
    public string? DocumentNo { get; set; }
    public Dictionary<string, string>? HeaderDta { get; set; }
    public List<MovementRow> LastMovements { get; set; } = [];
    public List<TransactionEvent> Events { get; set; } = [];
    public decimal? ExpectedFaConsumedKg { get; set; }
    public decimal? TargetFaConsumedKg { get; set; }
    public int? ExpectedFaMaterialStatus { get; set; }

    public T ReadRequest<T>() =>
        JsonSerializer.Deserialize<T>(RequestJson, SeparateOperationStore.JsonOptions)
        ?? throw new InvalidOperationException("Stored request could not be deserialized.");
}

public sealed record SeparateOperationResponse(
    string Kind,
    string ClientOperationId,
    string TransactionId,
    string Status,
    string Stage,
    string Message,
    string? DocumentNo,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MovementRow> Movements,
    decimal? TargetFaConsumedKg = null);

public static class SeparateOperationMapping
{
    public static SeparateOperationResponse ToResponse(this SeparateOperation tx) => new(
        tx.Kind,
        tx.ClientOperationId,
        tx.TransactionId,
        tx.Status,
        tx.Stage,
        tx.Message,
        tx.DocumentNo,
        tx.UpdatedAt,
        tx.LastMovements,
        tx.TargetFaConsumedKg);
}

public sealed class SeparateOperationStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SeparateOperationStore(IWebHostEnvironment env)
    {
        _directory = Path.Combine(env.ContentRootPath, "App_Data", "process-transactions");
        Directory.CreateDirectory(_directory);
    }

    public SemaphoreSlim GetLock(string kind, string id) =>
        _locks.GetOrAdd(kind + ":" + id, _ => new SemaphoreSlim(1, 1));

    public async Task<SeparateOperation?> GetAsync(string kind, string id, CancellationToken ct)
    {
        var path = PathFor(kind, id);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SeparateOperation>(stream, JsonOptions, ct);
    }

    public async Task SaveAsync(SeparateOperation tx, CancellationToken ct)
    {
        tx.UpdatedAt = DateTimeOffset.UtcNow;
        var path = PathFor(tx.Kind, tx.ClientOperationId);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, tx, JsonOptions, ct);
        File.Move(temp, path, true);
    }

    public static string SerializeRequest<T>(T request) => JsonSerializer.Serialize(request, JsonOptions);

    private string PathFor(string kind, string id)
    {
        static string Safe(string value) => string.Concat((value ?? "").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        var k = Safe(kind);
        var i = Safe(id);
        if (k.Length == 0 || i.Length == 0) throw new ArgumentException("Invalid transaction key.");
        return Path.Combine(_directory, k + "_" + i + ".json");
    }
}

public sealed class ProcessConflictException(string message) : Exception(message);

internal sealed record TransferSpec(
    int Position,
    string BookingKey,
    string Article,
    string ArticleText,
    string FromWarehouse,
    string FromWarehouseText,
    string FromStorageBin,
    string FromBatch,
    string ToWarehouse,
    string ToWarehouseText,
    string ToStorageBin,
    string ToBatch,
    decimal QuantityKg,
    DateOnly? ProductionDate = null);

internal sealed record ExpectedTransferMovement(
    string Position,
    string BookingKey,
    string Article,
    string Batch,
    string Warehouse,
    string StorageBin,
    decimal QuantityKg);

public sealed class MaterialTransferBookingService
{
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly SeparateOperationStore _store;

    public MaterialTransferBookingService(
        OxaionClient oxaion,
        IOptions<OxaionOptions> options,
        SeparateOperationStore store)
    {
        _oxaion = oxaion;
        _options = options.Value;
        _store = store;
    }

    public async Task BookAsync(
        SeparateOperation tx,
        DateOnly bookingDate,
        string personnelNo,
        string personnelName,
        string bookingText,
        IReadOnlyList<TransferSpec> specs,
        CancellationToken ct)
    {
        if (specs.Count == 0) throw new ArgumentException("At least one transfer position is required.");
        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var header = await NewHeaderAsync(session, ct);
            await SaveEventAsync(tx, "HEADER_SUBMITTING", "Creating Oxaion material document header.", ct);
            var op = OperatorContext(tx, personnelNo, personnelName, bookingText);
            var put = await session.CallAsync("LB20100J", "*PUTNEW", Merge(header, Dict(
                ("KOBGDT", Iso(bookingDate)),
                ("KOBGT1", op.Operator),
                ("KOBGTX", op.BookingText),
                ("KOBGKZ", "MB"),
                ("KOFIRM", _options.Firm),
                ("KEYTYPE", "C_LKOPF"))), ct);
            OxaionSession.AssertNoFcod(put);
            var doc = Get(put.Dta, "KOBGNR");
            if (string.IsNullOrWhiteSpace(doc)) throw new InvalidOperationException("LB20100J *PUTNEW did not return KOBGNR.");
            tx.DocumentNo = doc;
            tx.HeaderDta = put.Dta;
            tx.Status = TransactionStatuses.SendingToOxaion;
            await SaveEventAsync(tx, "HEADER_CREATED", $"Oxaion material document {doc} created.", ct);

            var context = await OpenExistingDocumentAsync(session, tx, ct);
            Dictionary<string, string> previous = tx.HeaderDta;
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var validated = await AddPositionAsync(session, tx, context.Ssid, previous, bookingDate, op, spec, ct);
                if (i + 1 < specs.Count)
                {
                    var refreshed = await ReadDocumentListAsync(session, context.Ssid, ct);
                    previous = TargetAccessStateFromRows(spec, refreshed.Xml, validated);
                }
            }

            await SaveEventAsync(tx, "ENDING", "Closing Oxaion material document.", ct);
            OxaionSession.AssertNoFcod(await session.CallAsync(
                "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo), ("KEYTYPE", "LKOPF")), ct));
            await VerifyAndCloseAsync(session, tx, specs, ct);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tx.DocumentNo))
            {
                try
                {
                    await session.CallAsync(
                        "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None);
                }
                catch
                {
                    // Cleanup is best-effort only. The original exception/status remains authoritative.
                }
            }
        }
    }

    public async Task<bool> ReconcileAsync(SeparateOperation tx, IReadOnlyList<TransferSpec> specs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tx.DocumentNo) || tx.HeaderDta is null) return false;
        await using var session = await _oxaion.ConnectAsync(ct);
        try
        {
            var context = await OpenExistingDocumentAsync(session, tx, ct);
            var rows = MixBookingService.ParseMovements(context.List.Xml);
            tx.LastMovements = rows.ToList();
            if (!MovementsComplete(specs, rows, out var message))
            {
                tx.Status = TransactionStatuses.ManualReviewRequired;
                await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", message, ct);
                try
                {
                    OxaionSession.AssertNoFcod(await session.CallAsync(
                        "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None));
                }
                catch { }
                return false;
            }

            OxaionSession.AssertNoFcod(await session.CallAsync(
                "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None));
            tx.Status = TransactionStatuses.Success;
            await SaveEventAsync(tx, "SUCCESS", "Oxaion material movements were found exactly once and the verification view was closed.", CancellationToken.None);
            return true;
        }
        catch (OxaionTransportException ex)
        {
            tx.Status = TransactionStatuses.Uncertain;
            await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct);
            return false;
        }
        catch (Exception ex)
        {
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct);
            return false;
        }
    }

    internal static bool MovementsComplete(IReadOnlyList<TransferSpec> specs, IReadOnlyList<MovementRow> rows, out string message)
    {
        var expected = BuildExpected(specs);
        if (rows.Count != expected.Count)
        {
            message = $"Expected {expected.Count} movement rows, found {rows.Count}. No automatic retry is allowed.";
            return false;
        }
        foreach (var e in expected)
        {
            var count = rows.Count(r =>
                r.Position == e.Position && r.BookingKey == e.BookingKey && r.Article == e.Article &&
                r.Batch == e.Batch && r.Warehouse == e.Warehouse &&
                (r.StorageBin ?? "") == (e.StorageBin ?? "") && Math.Abs(r.Quantity - e.QuantityKg) < 0.0005m);
            if (count != 1)
            {
                message = $"Expected movement {e.Position}/{e.BookingKey}/{e.Warehouse}/{e.StorageBin}/{e.Batch}/{e.QuantityKg:0.###} kg was found {count} times.";
                return false;
            }
        }
        message = "All expected movements are present exactly once.";
        return true;
    }

    private async Task VerifyAndCloseAsync(OxaionSession session, SeparateOperation tx, IReadOnlyList<TransferSpec> specs, CancellationToken ct)
    {
        await SaveEventAsync(tx, "VERIFYING", "Reopening Oxaion material document for exact movement verification.", ct);
        var context = await OpenExistingDocumentAsync(session, tx, ct);
        Exception? verificationFailure = null;
        try
        {
            var rows = MixBookingService.ParseMovements(context.List.Xml);
            tx.LastMovements = rows.ToList();
            if (!MovementsComplete(specs, rows, out var message))
                verificationFailure = new InvalidOperationException("Final verification failed: " + message);
        }
        catch (Exception ex)
        {
            verificationFailure = ex;
        }

        // OPEN for final verification holds the material document. Always close the verification
        // view, including when the movement comparison itself failed. A disconnect is not a
        // substitute for LB20100J *END.
        try
        {
            var end = await session.CallAsync(
                "LB20100J", "*END", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF")), CancellationToken.None);
            OxaionSession.AssertNoFcod(end);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Oxaion document {tx.DocumentNo} may already contain material movements, but the verification view could not be closed safely. Do not rebook; check the Oxaion lock and movements manually.", ex);
        }

        if (verificationFailure is not null) throw verificationFailure;

        tx.Status = TransactionStatuses.Success;
        await SaveEventAsync(tx, "SUCCESS", $"Oxaion document {tx.DocumentNo} verified with exactly {tx.LastMovements.Count} expected movements and explicitly closed.", CancellationToken.None);
    }

    private async Task ValidateLfDestinationAsync(OxaionSession session, Dictionary<string, string> positionState, TransferSpec spec, CancellationToken ct)
    {
        var warehouseF4 = Merge(positionState, Dict(
            ("PFIELD", "TX_LAGO2"), ("FIELD", "TX_LAG2"), ("MFLD", "TX_LAG2")));
        var warehouseLookup = await session.CallAsync("LB20115J", "*F4", warehouseF4, ct);
        OxaionSession.AssertNoFcod(warehouseLookup);
        var warehouseSsid = Get(warehouseLookup.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(warehouseSsid) || !string.Equals(Get(warehouseLookup.Dta, "PGMN"), "US16601R", StringComparison.Ordinal))
            throw new InvalidOperationException("LB20115J target-warehouse F4 did not open the confirmed US16601R list.");

        OxaionSession.AssertNoFcod(await session.CallAsync("US16601R", "*GETHDR", Dict(
            ("FLD", "TX_LAG2"), ("CPY-FRSSID", ""), ("NOHWPgm", "US16601"),
            ("SSID", warehouseSsid), ("PFLD", "TX_LAGO2")), ct));
        var warehouses = await session.CallAsync("US16601R", "*FIRSTLIST", Dict(
            ("FLD", "TX_LAG2"), ("SSID", warehouseSsid), ("PFLD", "TX_LAGO2"), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(warehouses);
        if (!MachineStockService.HasStop(warehouses.Xml))
            throw new InvalidOperationException("US16601R target-warehouse list did not return STOP; incomplete target validation is not accepted.");

        var warehouseMatches = warehouses.Xml.Descendants("ROW").Where(row =>
            string.Equals(row.Element("KEY")?.Element("TX_LAG2")?.Value.Trim(), spec.ToWarehouse, StringComparison.OrdinalIgnoreCase)).ToList();
        if (warehouseMatches.Count != 1)
            throw new ProcessConflictException($"Ziel-Lagerort {spec.ToWarehouse} wurde in der bestätigten Oxaion-Lagerortliste nicht eindeutig gefunden.");
        var binManaged = string.Equals(warehouseMatches[0].Element("ULGSTP.LGKLPL")?.Value.Trim(), "J", StringComparison.OrdinalIgnoreCase);
        if (binManaged && string.IsNullOrWhiteSpace(spec.ToStorageBin))
            throw new ProcessConflictException($"Ziel-Lagerort {spec.ToWarehouse} ist lagerplatzgeführt. Ein Oxaion-Lagerplatz ist erforderlich.");
        if (!binManaged && !string.IsNullOrWhiteSpace(spec.ToStorageBin))
            throw new ProcessConflictException($"Ziel-Lagerort {spec.ToWarehouse} ist laut Oxaion nicht lagerplatzgeführt. Es darf kein Lagerplatz übergeben werden.");
        if (!binManaged) return;

        var binF4 = Merge(positionState, Dict(
            ("PFIELD", "*NONE *NONE *NONE TX_LAG2 TX_LAP2"),
            ("FIELD", "*NONE *NONE *NONE *NONE *NONE"),
            ("MFLD", "TX_LAP2"), ("TX_LAG2", spec.ToWarehouse), ("TX_LAP2", spec.ToStorageBin)));
        var binLookup = await session.CallAsync("LB20115J", "*F4", binF4, ct);
        OxaionSession.AssertNoFcod(binLookup);
        var binSsid = Get(binLookup.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(binSsid) || !string.Equals(Get(binLookup.Dta, "PGMN"), "LB13210R", StringComparison.Ordinal))
            throw new InvalidOperationException("LB20115J target-bin F4 did not open the confirmed LB13210R list.");

        const string noFields = "*NONE *NONE *NONE *NONE *NONE";
        const string parentFields = "*NONE *NONE *NONE TX_LAG2 TX_LAP2";
        OxaionSession.AssertNoFcod(await session.CallAsync("LB13210R", "*GETHDR", Dict(
            ("FLD", noFields), ("CPY-FRSSID", ""), ("NOHWPgm", "LB13210"),
            ("SSID", binSsid), ("PFLD", parentFields)), ct));
        var bins = await session.CallAsync("LB13210R", "*FIRSTLIST", Dict(
            ("FLD", noFields), ("SSID", binSsid), ("PFLD", parentFields), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(bins);
        if (!MachineStockService.HasStop(bins.Xml))
            throw new InvalidOperationException("LB13210R target-bin list did not return STOP; incomplete target validation is not accepted.");
        var binMatches = bins.Xml.Descendants("ROW").Count(row =>
            string.Equals(row.Element("KEY")?.Element("TX_LAG2")?.Value.Trim(), spec.ToWarehouse, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Element("KEY")?.Element("TX_LAP2")?.Value.Trim(), spec.ToStorageBin, StringComparison.Ordinal));
        if (binMatches != 1)
            throw new ProcessConflictException($"Ziel-Lagerplatz {spec.ToWarehouse} / {spec.ToStorageBin} wurde in der bestätigten Oxaion-Lagerplatzliste nicht eindeutig gefunden.");
    }

    private async Task<Dictionary<string, string>> NewHeaderAsync(OxaionSession session, CancellationToken ct)
    {
        var load = await session.CallAsync("LB20100J", "*LOADNEW", Dict(
            ("ISSID", "HTTPWEB" + Guid.NewGuid().ToString("N")),
            ("NOHWPgm", "LB20100"), ("SSID", ""), ("KOBGNR", ""), ("KEYTYPE", "C_LKOPF")), ct);
        OxaionSession.AssertNoFcod(load);
        var created = await session.CallAsync("LB20100J", "*NEW", Merge(load.Dta, Dict(("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(created);
        return created.Dta;
    }

    private async Task<Dictionary<string, string>> AddPositionAsync(
        OxaionSession session,
        SeparateOperation tx,
        string ssid,
        Dictionary<string, string> previous,
        DateOnly bookingDate,
        (string Operator, string BookingText) op,
        TransferSpec spec,
        CancellationToken ct)
    {
        var position = spec.Position.ToString(CultureInfo.InvariantCulture);
        await SaveEventAsync(tx, $"POSITION_{position}_VALIDATING", $"Validating {spec.BookingKey} position {position}.", ct);
        var seed = Merge(previous, Dict(
            ("SSID", ssid), ("SNR", position), ("WSTR", "1"), ("PGMN", "LB20110R"), ("MTYPE", "*PGM"),
            ("NAME", "UPOSTP.POPONR"), ("MC-Modus", "true"), ("NoModDlg", "true"),
            ("keyFields", "PSBGNR PSPOSI PSKOPO PSBGZT"), ("keyFirm", "FIRM"), ("KEYTYPE", "C_LKOPF")));
        var created = await session.CallAsync("LB20115J", "*NEW", seed, ct);
        OxaionSession.AssertNoFcod(created);
        var state = Merge(seed, created.Dta);

        var first = Merge(state, PositionFields(tx, bookingDate, op, spec, "0,000", FormatQty(spec.QuantityKg), "J"));
        var r1 = await session.CallAsync("LB20115J", "*PUTNEW", first, ct);
        OxaionSession.AssertNoFcod(r1);
        state = Merge(first, r1.Dta);

        if (string.Equals(spec.BookingKey, "LF", StringComparison.Ordinal))
            await ValidateLfDestinationAsync(session, state, spec, ct);

        var tcode = FirstText(r1.Xml, "TCODE");
        var q1 = FirstText(r1.Xml, "PSBMN1");
        var q2 = FirstText(r1.Xml, "PSBMN2");
        var newTs = FirstText(r1.Xml, "PSBGZT");
        Dictionary<string, string> validated;
        if (tcode == "WIN3")
        {
            var win = await session.CallAsync("LB20115J", "*LOADWIN3", Dict(("NOHWPgm", "LB201153"), ("NoHints", "")), ct);
            OxaionSession.AssertNoFcod(win);
            state = Merge(state, win.Dta);
            var final = Merge(state, PositionFields(tx, bookingDate, op, spec, FormatQty(spec.QuantityKg), FormatQty(spec.QuantityKg), "N"));
            final["NoVPDialog"] = "true";
            final["NoWinSnnr"] = "true";
            final["NoWindow"] = "true";
            var fr = await session.CallAsync("LB20115J", "*PUTNEW", final, ct);
            OxaionSession.AssertNoFcod(fr);
            validated = Merge(final, fr.Dta);
        }
        else if (!string.IsNullOrWhiteSpace(newTs)
                 && newTs != Get(first, "PSBGZT")
                 && !string.IsNullOrWhiteSpace(q1)
                 && !string.IsNullOrWhiteSpace(q2))
        {
            validated = state;
        }
        else
        {
            throw new InvalidOperationException($"Position {position} PUTNEW returned neither TCODE=WIN3 nor the proven final HTTP state.");
        }

        tx.Status = TransactionStatuses.SendingToOxaion;
        await SaveEventAsync(tx, $"POSITION_{position}_UPD_SENT", $"Submitting LB20110R *UPD for position {position}.", ct);
        var update = await session.CallAsync("LB20110R", "*UPD", Merge(validated, Dict(("SSID", ssid), ("mode", "update"), ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(update);
        var rows = MixBookingService.ParseMovements(update.Xml);
        var pairSpecs = new[] { spec };
        if (!MovementsComplete(pairSpecs, rows.Where(r => r.Position == position).ToList(), out var message))
            throw new InvalidOperationException($"LB20110R *UPD did not return the expected pair for position {position}: {message}");
        await SaveEventAsync(tx, $"POSITION_{position}_CONFIRMED", $"Position {position} confirmed by Oxaion.", ct);
        return validated;
    }

    private Dictionary<string, string> PositionFields(
        SeparateOperation tx,
        DateOnly bookingDate,
        (string Operator, string BookingText) op,
        TransferSpec spec,
        string q1,
        string q2,
        string first)
    {
        var fields = Dict(
            ("PSANWG", "LBS"), ("PSBGKZ", "MB"), ("PSBGNR", tx.DocumentNo!), ("PSBGDT", Iso(bookingDate)),
            ("PSBGTX", op.BookingText), ("TX_BGT1", op.Operator), ("PSFIRM", _options.Firm),
            ("PSPOSI", spec.Position.ToString(CultureInfo.InvariantCulture)), ("PSBWKZ", spec.BookingKey),
            ("PSIDNR", spec.Article), ("I_PSIDNR", spec.Article), ("DEMO_IDNR", spec.Article), ("POIDNR", spec.Article),
            ("I_TX_IDN2", ""), ("I_TX_PCKMM", ""), ("I_TX_PCKMS", ""), ("I_TX_PCKMZ", ""),
            ("PSLAGO", spec.FromWarehouse), ("PSPONR", spec.FromBatch), ("PSLAPL", spec.FromStorageBin ?? ""),
            ("TX_LAG2", spec.ToWarehouse), ("TX_PON2", spec.ToBatch ?? ""),
            ("PSBMN1", q1), ("PSBMN2", q2), ("TX_FIRST", first),
            ("TX_LAGO", TextOrCode(spec.FromWarehouseText, spec.FromWarehouse)),
            ("TX_LAGO2", TextOrCode(spec.ToWarehouseText, spec.ToWarehouse)), ("TX_PDBZ2", "pro 1"),
            ("KEYTYPE", "C_LKOPF"), ("mode", "merge"));
        if (!string.IsNullOrWhiteSpace(spec.ToStorageBin)) fields["TX_LAP2"] = spec.ToStorageBin;
        if (spec.ProductionDate is not null) fields["PSPRDT"] = Iso(spec.ProductionDate.Value);
        return fields;
    }

    private static Dictionary<string, string> TargetAccessStateFromRows(TransferSpec spec, XDocument xml, Dictionary<string, string> fallback)
    {
        var expectedPosition = spec.Position.ToString(CultureInfo.InvariantCulture);
        var accessKey = spec.BookingKey == "LM" ? "LN" : spec.BookingKey == "LF" ? "LE" : "";
        var expectedBatch = spec.BookingKey == "LM" ? spec.ToBatch : spec.FromBatch;
        foreach (var row in xml.Descendants("ROW"))
        {
            if (row.Element("LPSDAP.PSBWKZ")?.Value.Trim() != accessKey
                || row.Element("LPSDAP.PSPONR")?.Value.Trim() != expectedBatch)
                continue;
            var key = row.Element("KEY");
            var pos = row.Element("LPSDAP.PSPOSI")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(pos)) pos = key?.Element("PSPOSI")?.Value.Trim();
            if (pos != expectedPosition) continue;
            var ts = key?.Element("PSBGZT")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(ts)) continue;
            var state = new Dictionary<string, string>(fallback, StringComparer.Ordinal)
            {
                ["PSBGZT"] = ts,
                ["PSPOSI"] = expectedPosition
            };
            var kopo = key?.Element("PSKOPO")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(kopo)) state["PSKOPO"] = kopo;
            return state;
        }
        throw new InvalidOperationException($"Persisted access row for position {spec.Position} was not found.");
    }

    private static IReadOnlyList<ExpectedTransferMovement> BuildExpected(IReadOnlyList<TransferSpec> specs)
    {
        var expected = new List<ExpectedTransferMovement>(specs.Count * 2);
        foreach (var s in specs)
        {
            var p = s.Position.ToString(CultureInfo.InvariantCulture);
            expected.Add(new ExpectedTransferMovement(p, s.BookingKey, s.Article, s.FromBatch, s.FromWarehouse, s.FromStorageBin ?? "", s.QuantityKg));
            if (s.BookingKey == "LM")
                expected.Add(new ExpectedTransferMovement(p, "LN", s.Article, s.ToBatch, s.ToWarehouse, s.ToStorageBin ?? "", s.QuantityKg));
            else if (s.BookingKey == "LF")
                expected.Add(new ExpectedTransferMovement(p, "LE", s.Article, s.FromBatch, s.ToWarehouse, s.ToStorageBin ?? "", s.QuantityKg));
            else
                throw new InvalidOperationException("Unsupported transfer booking key " + s.BookingKey);
        }
        return expected;
    }

    private async Task<DocumentContext> OpenExistingDocumentAsync(OxaionSession session, SeparateOperation tx, CancellationToken ct)
    {
        var open = await session.CallAsync("LB20100J", "*OPEN", Dict(("KOBGNR", tx.DocumentNo!), ("KEYTYPE", "LKOPF"), ("noAutCheck", "")), ct);
        OxaionSession.AssertNoFcod(open);
        var shortResult = await session.CallAsync("LB20090J", "*SHORT", Merge(tx.HeaderDta!, Dict(
            ("PSANWG", "LBS"), ("PSBGNR", tx.DocumentNo!), ("KEYTYPE", "C_LKOPF"))), ct);
        OxaionSession.AssertNoFcod(shortResult);
        var ssid = Get(shortResult.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("LB20090J *SHORT did not return SSID.");
        OxaionSession.AssertNoFcod(await session.CallAsync("LB20110R", "*GETHDR", Dict(("SSID", ssid)), ct));
        var list = await ReadDocumentListAsync(session, ssid, ct);
        return new DocumentContext(ssid, list);
    }

    private static async Task<OxaionCallResult> ReadDocumentListAsync(OxaionSession session, string ssid, CancellationToken ct)
    {
        var list = await session.CallAsync("LB20110R", "*FIRSTLIST", Dict(("FLD", ""), ("PFLD", ""), ("SSID", ssid), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!MachineStockService.HasStop(list.Xml))
            throw new InvalidOperationException("LB20110R material-document list did not return STOP; incomplete verification is not accepted.");
        return list;
    }

    private static (string Operator, string BookingText) OperatorContext(
        SeparateOperation tx, string personnelNo, string personnelName, string bookingText)
    {
        var operatorText = string.IsNullOrWhiteSpace(personnelName) ? $"PN {personnelNo}" : $"PN {personnelNo} | {personnelName}";
        var prefix = $"{tx.TransactionId[..12]}|PN{personnelNo}";
        var room = Math.Max(0, 50 - prefix.Length - 1);
        var text = room > 0 && !string.IsNullOrWhiteSpace(bookingText)
            ? prefix + "|" + bookingText[..Math.Min(room, bookingText.Length)]
            : prefix;
        return (operatorText, text);
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    {
        tx.Stage = stage;
        tx.Message = message;
        tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message));
        await _store.SaveAsync(tx, ct);
    }

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) =>
        values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        var result = new Dictionary<string, string>(left, StringComparer.Ordinal);
        foreach (var item in right) result[item.Key] = item.Value ?? "";
        return result;
    }
    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var v) ? v : "";
    private static string FirstText(XDocument xml, string name) => xml.Descendants(name).FirstOrDefault()?.Value.Trim() ?? "";
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string FormatQty(decimal v) => v.ToString("0.000", CultureInfo.GetCultureInfo("de-AT"));
    private static string TextOrCode(string text, string code) => string.IsNullOrWhiteSpace(text) ? code : text;
    private sealed record DocumentContext(string Ssid, OxaionCallResult List);
}

public sealed class TankOutService
{
    public const string Kind = "tank-out";
    private readonly SeparateOperationStore _store;
    private readonly MaterialTransferBookingService _booking;
    private readonly MachineTankService _tanks;
    private readonly PersonnelService _personnel;
    private readonly OxaionClient _oxaion;

    public TankOutService(SeparateOperationStore store, MaterialTransferBookingService booking, MachineTankService tanks, PersonnelService personnel, OxaionClient oxaion)
    {
        _store = store; _booking = booking; _tanks = tanks; _personnel = personnel; _oxaion = oxaion;
    }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(TankOutRequest request, CancellationToken ct)
    {
        Validate(request);
        var gate = _store.GetLock(Kind, request.ClientOperationId);
        await gate.WaitAsync(ct);
        try
        {
            var json = SeparateOperationStore.SerializeRequest(request);
            var existing = await _store.GetAsync(Kind, request.ClientOperationId, ct);
            if (existing is not null)
            {
                if (existing.RequestJson != json) throw new ProcessConflictException("clientOperationId already belongs to different tank-out data.");
                return existing;
            }
            var tx = NewOperation(Kind, request.ClientOperationId, json);
            await SaveEventAsync(tx, "CREATED", "Tank-out transaction created.", ct);
            try
            {
                await ValidatePersonnelAsync(request.PersonnelNo, request.PersonnelName, ct);
                var stock = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (stock.Status != MachineStockStatuses.Unique || stock.Rows.Count != 1)
                    throw new ProcessConflictException(stock.Message);
                var current = stock.Rows[0];
                if (!StockEquals(current, request.Article, request.Batch, request.QuantityKg))
                    throw new ProcessConflictException($"Tank stock changed. Current: {current.Article}, {current.Batch}, {current.QuantityKg:0.###} kg.");

                var targetText = await ResolveWarehouseTextAsync(request.TargetWarehouse, ct);
                var specs = new[]
                {
                    new TransferSpec(1, "LF", request.Article, request.ArticleText,
                        request.TankWarehouse, request.TankWarehouseText, "", request.Batch,
                        request.TargetWarehouse, targetText, request.TargetStorageBin, "", request.QuantityKg)
                };
                await _booking.BookAsync(tx, DateOnly.FromDateTime(DateTime.Today), request.PersonnelNo, request.PersonnelName,
                    "Pulver aus Tank auf Lagerplatz", specs, ct);
            }
            catch (ProcessConflictException ex) { tx.Status = TransactionStatuses.Conflict; await SaveEventAsync(tx, "CONFLICT", ex.Message, ct); }
            catch (OxaionRejectedException ex) { tx.Status = TransactionStatuses.Rejected; await SaveEventAsync(tx, "REJECTED", ex.Message, ct); }
            catch (OxaionTransportException ex) { tx.Status = TransactionStatuses.Uncertain; await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct); }
            catch (Exception ex) { tx.Status = TransactionStatuses.ManualReviewRequired; await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct); }
            return tx;
        }
        finally { gate.Release(); }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        if (string.IsNullOrWhiteSpace(tx.DocumentNo) || tx.HeaderDta is null)
        {
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", "No confirmed Oxaion material document is available. Do not rebook blindly.", ct);
            return tx;
        }
        var request = tx.ReadRequest<TankOutRequest>();
        var targetText = await ResolveWarehouseTextAsync(request.TargetWarehouse, ct);
        var specs = new[]
        {
            new TransferSpec(1, "LF", request.Article, request.ArticleText,
                request.TankWarehouse, request.TankWarehouseText, "", request.Batch,
                request.TargetWarehouse, targetText, request.TargetStorageBin, "", request.QuantityKg)
        };
        await _booking.ReconcileAsync(tx, specs, ct);
        return tx;
    }

    private async Task<string> ResolveWarehouseTextAsync(string warehouse, CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);
        var plain = await session.CallAsync("US00006J", "*GETPLAIN", new Dictionary<string, string>
        {
            ["MFLD"] = "LAGO", ["PGMN"] = "US30600J", ["LAGO"] = warehouse,
            ["PFIELD"] = "TX_LAGO", ["FIELD"] = "LAGO"
        }, ct);
        OxaionSession.AssertNoFcod(plain);
        return plain.Dta.TryGetValue("TX_LAGO", out var text) && !string.IsNullOrWhiteSpace(text) ? text : warehouse;
    }

    private async Task ValidatePersonnelAsync(string no, string name, CancellationToken ct)
    {
        var employee = await _personnel.ReadExactAsync(no, ct);
        if (employee is null || employee.FullName != name) throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");
    }

    private static void Validate(TankOutRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo) || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankWarehouse) || string.IsNullOrWhiteSpace(r.Batch)
            || string.IsNullOrWhiteSpace(r.TargetWarehouse) || string.IsNullOrWhiteSpace(r.TargetStorageBin))
            throw new ArgumentException("Tank, article/batch, target warehouse/storage bin and personnel are required.");
        if (r.QuantityKg <= 0) throw new ArgumentException("Tank quantity must be > 0.");
        if (string.Equals(r.TankWarehouse, r.TargetWarehouse, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Target warehouse must differ from machine tank warehouse.");
    }

    private static bool StockEquals(MachineStockRow row, string article, string batch, decimal qty) =>
        row.Article.Equals(article, StringComparison.OrdinalIgnoreCase) && row.Batch == batch && Math.Abs(row.QuantityKg - qty) < 0.0005m;
    private static SeparateOperation NewOperation(string kind, string id, string json) => new() { Kind = kind, ClientOperationId = id, RequestJson = json };
    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    { tx.Stage = stage; tx.Message = message; tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message)); await _store.SaveAsync(tx, ct); }
}

public sealed class FillNewService
{
    public const string Kind = "fill-new";
    private readonly SeparateOperationStore _store;
    private readonly MaterialTransferBookingService _booking;
    private readonly MachineTankService _tanks;
    private readonly SourceStockService _sources;
    private readonly PersonnelService _personnel;

    public FillNewService(SeparateOperationStore store, MaterialTransferBookingService booking, MachineTankService tanks, SourceStockService sources, PersonnelService personnel)
    { _store = store; _booking = booking; _tanks = tanks; _sources = sources; _personnel = personnel; }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(FillNewRequest request, CancellationToken ct)
    {
        Validate(request);
        var gate = _store.GetLock(Kind, request.ClientOperationId);
        await gate.WaitAsync(ct);
        try
        {
            var json = SeparateOperationStore.SerializeRequest(request);
            var existing = await _store.GetAsync(Kind, request.ClientOperationId, ct);
            if (existing is not null)
            {
                if (existing.RequestJson != json) throw new ProcessConflictException("clientOperationId already belongs to different fill-new data.");
                return existing;
            }
            var tx = new SeparateOperation { Kind = Kind, ClientOperationId = request.ClientOperationId, RequestJson = json };
            await SaveEventAsync(tx, "CREATED", "Fill-new transaction created.", ct);
            try
            {
                var employee = await _personnel.ReadExactAsync(request.PersonnelNo, ct);
                if (employee is null || employee.FullName != request.PersonnelName)
                    throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");
                var tank = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (tank.Status != MachineStockStatuses.Empty)
                    throw new ProcessConflictException("Maschinentank ist vor der Neubefüllung nicht mehr eindeutig leer. " + tank.Message);
                var validation = await _sources.ValidateSourcesAsync(request.Article, request.Sources, ct);
                if (!validation.IsValid) throw new ProcessConflictException(validation.Message);

                var specs = request.Sources.Select((s, i) => new TransferSpec(
                    i + 1, "LM", request.Article, request.ArticleText,
                    s.Warehouse, s.WarehouseText, s.StorageBin ?? "", s.Batch,
                    request.TankWarehouse, request.TankWarehouseText, "", request.TargetBatch,
                    s.AmountKg, i == 0 ? request.ProductionDate : null)).ToArray();
                await _booking.BookAsync(tx, request.BookingDate, request.PersonnelNo, request.PersonnelName,
                    "Pulver in Maschinentank", specs, ct);
            }
            catch (ProcessConflictException ex) { tx.Status = TransactionStatuses.Conflict; await SaveEventAsync(tx, "CONFLICT", ex.Message, ct); }
            catch (OxaionRejectedException ex) { tx.Status = TransactionStatuses.Rejected; await SaveEventAsync(tx, "REJECTED", ex.Message, ct); }
            catch (OxaionTransportException ex) { tx.Status = TransactionStatuses.Uncertain; await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct); }
            catch (Exception ex) { tx.Status = TransactionStatuses.ManualReviewRequired; await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct); }
            return tx;
        }
        finally { gate.Release(); }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        var request = tx.ReadRequest<FillNewRequest>();
        var specs = request.Sources.Select((s, i) => new TransferSpec(
            i + 1, "LM", request.Article, request.ArticleText,
            s.Warehouse, s.WarehouseText, s.StorageBin ?? "", s.Batch,
            request.TankWarehouse, request.TankWarehouseText, "", request.TargetBatch,
            s.AmountKg, i == 0 ? request.ProductionDate : null)).ToArray();
        await _booking.ReconcileAsync(tx, specs, ct);
        return tx;
    }

    private static void Validate(FillNewRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo) || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankWarehouse) || string.IsNullOrWhiteSpace(r.TargetBatch))
            throw new ArgumentException("Operation, personnel, tank, article and target MIX batch are required.");
        if (r.Sources is null || r.Sources.Count == 0) throw new ArgumentException("At least one powder source is required.");
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (r.BookingDate != today || r.ProductionDate != today) throw new ArgumentException("Booking and MIX production date must be today.");
        if (!ReplenishmentRules.IsValidGeneratedMixBatch(r.Article, r.TargetBatch, r.ProductionDate))
            throw new ArgumentException("Target MIX batch does not match the confirmed generated MIX schema.");
        foreach (var s in r.Sources)
        {
            if (s.AmountKg <= 0 || string.IsNullOrWhiteSpace(s.Warehouse) || string.IsNullOrWhiteSpace(s.Batch))
                throw new ArgumentException("Every source requires warehouse, batch and amount > 0.");
            if (string.Equals(s.Warehouse, r.TankWarehouse, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Machine tank cannot be a fill source.");
            if (string.Equals(s.Batch, r.TargetBatch, StringComparison.Ordinal))
                throw new ArgumentException("A new MIX batch must always differ from every source batch.");
        }
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    { tx.Stage = stage; tx.Message = message; tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message)); await _store.SaveAsync(tx, ct); }
}

public sealed class FaMaterialService
{
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;

    public FaMaterialService(OxaionClient oxaion, IOptions<OxaionOptions> options)
    { _oxaion = oxaion; _options = options.Value; }

    public async Task<FaMaterialPositionResult> FindUniqueAsync(string orderNo, string article, CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);
        return await FindUniqueAsync(session, orderNo, article, ct);
    }

    internal async Task<FaMaterialPositionResult> FindUniqueAsync(OxaionSession session, string orderNo, string article, CancellationToken ct)
    {
        orderNo = (orderNo ?? "").Trim(); article = (article ?? "").Trim();
        if (orderNo.Length == 0 || article.Length == 0) throw new ArgumentException("Fertigungsauftrag und Artikel sind erforderlich.");
        var initial = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
        var save = await session.CallAsync("PW20200", "*SAVSLT4CS", Dict(
            ("ISSID", initial), ("SSID", initial), ("AMPOSN", "0"), ("KEYTYPE", "C_PWAMA"),
            ("AMFAUN", orderNo), ("AMIDNK", "")), ct);
        OxaionSession.AssertNoFcod(save);
        var ssid = Get(save.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("PW20200 *SAVSLT4CS did not return SSID.");
        var cpy = Get(save.Dta, "CPY-FRSSID"); if (string.IsNullOrWhiteSpace(cpy)) cpy = initial;

        var keyInput = Merge(save.Dta, Dict(
            ("SSID", ssid), ("AMPOSN", "0"), ("PGMN", "PW20200"), ("AMIDNK", ""),
            ("KEYTYPE", "C_PWAMA"), ("ISSID", initial), ("init-filter", "false"),
            ("CPY-FRSSID", cpy), ("AMFAUN", orderNo)));
        var key = await session.CallAsync("PW20090J", "*SAVKEY", keyInput, ct);
        OxaionSession.AssertNoFcod(key);
        if (!string.IsNullOrWhiteSpace(Get(key.Dta, "SSID"))) ssid = Get(key.Dta, "SSID");

        var hdrInput = Merge(keyInput, key.Dta);
        foreach (var pair in Dict(
                     ("SSID", ssid), ("AMPOSN", "0"), ("PGMN", "PW20200"), ("AMIDNK", ""),
                     ("MODE", "XX18"), ("NOHWPgm", "PW20200R"), ("mode", "merge-attributes"),
                     ("KEYTYPE", "C_PWAMA"), ("ISSID", initial), ("init-filter", "false"),
                     ("FLD", ""), ("CPY-FRSSID", cpy), ("PFLD", ""), ("AMFAUN", orderNo))) hdrInput[pair.Key] = pair.Value;
        OxaionSession.AssertNoFcod(await session.CallAsync("PW20200R", "*GETHDR", hdrInput, ct));
        var list = await session.CallAsync("PW20200R", "*FIRSTLIST", Dict(("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!list.Xml.Descendants("STOP").Any()) throw new InvalidOperationException("PW20200R material list did not return STOP; incomplete result is not accepted.");

        var matches = ParseMaterialList(list.Xml, orderNo, article);
        if (matches.Count == 0) throw new ProcessConflictException($"Im Fertigungsauftrag {orderNo} wurde keine Materialposition für {article} gefunden.");
        if (matches.Count > 1) throw new ProcessConflictException($"Im Fertigungsauftrag {orderNo} wurden {matches.Count} Materialpositionen für {article} gefunden. Keine automatische Auswahl zulässig.");
        var m = matches[0];

        var readInput = Merge(hdrInput, Dict(
            ("SSID", ssid), ("AMPOSN", m.Position.ToString(CultureInfo.InvariantCulture)), ("PGMN", "PW20200"),
            ("AMIDNK", m.Article), ("MODE", "XX18"), ("NOHWPgm", "PW20200R"), ("mode", "merge-attributes"),
            ("KEYTYPE", "C_PWAMA"), ("ISSID", initial), ("init-filter", "false"), ("FLD", ""),
            ("AMFIRM", _options.Firm), ("CPY-FRSSID", cpy), ("PFLD", ""), ("AMFAUN", orderNo)));
        var read = await session.CallAsync("PW20201J", "*READ", readInput, ct);
        OxaionSession.AssertNoFcod(read);
        return ParseMaterialRead(read.Xml, orderNo, m.Position, article, m.ArticleText);
    }

    internal static IReadOnlyList<(int Position, string Article, string ArticleText)> ParseMaterialList(XDocument xml, string orderNo, string article)
    {
        var result = new List<(int, string, string)>();
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY"); if (key is null) continue;
            var fa = key.Element("AMFAUN")?.Value.Trim() ?? "";
            var id = key.Element("AMIDNK")?.Value.Trim() ?? "";
            var posText = key.Element("AMPOSN")?.Value.Trim() ?? "";
            if (!string.Equals(fa, orderNo, StringComparison.Ordinal) || !string.Equals(id, article, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(posText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos)) continue;
            var text = row.Element("AMIDNK_TLST.TLBEZG")?.Value.Trim() ?? "";
            result.Add((pos, id, text));
        }
        return result;
    }

    internal static FaMaterialPositionResult ParseMaterialRead(XDocument xml, string orderNo, int position, string article, string fallbackText)
    {
        foreach (var dta in xml.Descendants("DTA"))
        {
            string V(string n) => dta.Element(n)?.Value.Trim() ?? "";
            if (!string.Equals(V("AMFAUN"), orderNo, StringComparison.Ordinal)
                || !string.Equals(V("AMIDNK"), article, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(V("AMPOSN"), out var p) || p != position) continue;
            var status = int.TryParse(V("AMMPST"), out var s) ? s : -1;
            return new FaMaterialPositionResult(
                orderNo, position, article,
                string.IsNullOrWhiteSpace(V("TX_IDNK02")) ? fallbackText : V("TX_IDNK02"),
                ParseQty(V("AMMATB")), ParseQty(V("AMMATV")), V("AMMEKZ"), status, V("TX_MPST"),
                MkStatusAllowed(status), DateTimeOffset.UtcNow);
        }
        throw new InvalidOperationException("PW20201J *READ did not contain the exact requested material position.");
    }

    public static bool MkStatusAllowed(int status) => status is 0 or 1 or 8;
    public static decimal TargetConsumed(decimal alreadyConsumed, decimal additional) => alreadyConsumed + additional;

    internal static decimal ParseQty(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var x = value.Trim();
        if (x.Contains(',') && x.Contains('.')) x = x.LastIndexOf(',') > x.LastIndexOf('.') ? x.Replace(".", "").Replace(',', '.') : x.Replace(",", "");
        else x = x.Replace(',', '.');
        if (decimal.TryParse(x, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) return v;
        throw new FormatException("Invalid Oxaion material quantity: " + value);
    }

    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    { var r = new Dictionary<string, string>(a, StringComparer.Ordinal); foreach (var x in b) r[x.Key] = x.Value ?? ""; return r; }
    private static string Get(IReadOnlyDictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : "";
}

public sealed class FaConsumptionService
{
    public const string Kind = "fa-consumption";
    private readonly SeparateOperationStore _store;
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly FaMaterialService _materials;
    private readonly MachineTankService _tanks;
    private readonly PersonnelService _personnel;

    public FaConsumptionService(SeparateOperationStore store, OxaionClient oxaion, IOptions<OxaionOptions> options, FaMaterialService materials, MachineTankService tanks, PersonnelService personnel)
    { _store = store; _oxaion = oxaion; _options = options.Value; _materials = materials; _tanks = tanks; _personnel = personnel; }

    public Task<SeparateOperation?> GetAsync(string id, CancellationToken ct) => _store.GetAsync(Kind, id, ct);

    public async Task<SeparateOperation> ExecuteAsync(FaConsumptionRequest request, CancellationToken ct)
    {
        Validate(request);
        var gate = _store.GetLock(Kind, request.ClientOperationId);
        await gate.WaitAsync(ct);
        try
        {
            var json = SeparateOperationStore.SerializeRequest(request);
            var existing = await _store.GetAsync(Kind, request.ClientOperationId, ct);
            if (existing is not null)
            {
                if (existing.RequestJson != json) throw new ProcessConflictException("clientOperationId already belongs to different FA consumption data.");
                return existing;
            }
            var tx = new SeparateOperation
            {
                Kind = Kind, ClientOperationId = request.ClientOperationId, RequestJson = json,
                ExpectedFaConsumedKg = request.ExpectedConsumedKg,
                ExpectedFaMaterialStatus = request.ExpectedMaterialStatus,
                TargetFaConsumedKg = FaMaterialService.TargetConsumed(request.ExpectedConsumedKg, request.AdditionalConsumptionKg)
            };
            await SaveEventAsync(tx, "CREATED", "FA consumption transaction created.", ct);
            try
            {
                var employee = await _personnel.ReadExactAsync(request.PersonnelNo, ct);
                if (employee is null || employee.FullName != request.PersonnelName)
                    throw new ProcessConflictException("Mitarbeiter ist in Oxaion nicht mehr eindeutig bestätigt.");
                var tank = await _tanks.ReadStockAsync(request.TankWarehouse, ct);
                if (tank.Status != MachineStockStatuses.Unique || tank.Rows.Count != 1)
                    throw new ProcessConflictException("Tankbestand ist vor der FA-Buchung nicht mehr eindeutig. " + tank.Message);
                var t = tank.Rows[0];
                if (!t.Article.Equals(request.Article, StringComparison.OrdinalIgnoreCase) || t.Batch != request.TankBatch || Math.Abs(t.QuantityKg - request.TankQuantityKg) >= 0.0005m)
                    throw new ProcessConflictException($"Tankbestand hat sich seit der Anzeige geändert. Aktuell {t.Article}/{t.Batch}/{t.QuantityKg:0.###} kg.");
                if (request.AdditionalConsumptionKg > t.QuantityKg + 0.0005m)
                    throw new ProcessConflictException("Zusätzlicher Verbrauch ist größer als der aktuelle Tankbestand.");

                await using var session = await _oxaion.ConnectAsync(ct);
                var current = await _materials.FindUniqueAsync(session, request.OrderNo, request.Article, ct);
                if (current.MaterialPosition != request.MaterialPosition
                    || Math.Abs(current.RequiredKg - request.ExpectedRequiredKg) >= 0.0005m
                    || Math.Abs(current.ConsumedKg - request.ExpectedConsumedKg) >= 0.0005m
                    || current.MaterialStatus != request.ExpectedMaterialStatus)
                    throw new ProcessConflictException($"Materialposition hat sich seit der Anzeige geändert. Aktuell Pos. {current.MaterialPosition}, Soll {current.RequiredKg:0.###} kg, gebucht {current.ConsumedKg:0.###} kg, Status {current.MaterialStatus} {current.MaterialStatusText}.");
                if (!current.MkBookingAllowed)
                    throw new ProcessConflictException($"Materialposition {current.MaterialPosition} hat Status {current.MaterialStatus} {current.MaterialStatusText}. MK ist nur bei Status 0, 1 oder 8 zulässig. MU wird in dieser App noch nicht ausgeführt.");

                tx.Status = TransactionStatuses.SendingToOxaion;
                await SaveEventAsync(tx, "MK_SUBMITTING", $"Submitting additional {request.AdditionalConsumptionKg:0.###} kg to FA {request.OrderNo}, material position {request.MaterialPosition}.", ct);
                await SubmitMkAsync(session, request, current, tx.TargetFaConsumedKg!.Value, ct);

                var verified = await _materials.FindUniqueAsync(session, request.OrderNo, request.Article, ct);
                if (verified.MaterialPosition != request.MaterialPosition
                    || Math.Abs(verified.ConsumedKg - tx.TargetFaConsumedKg.Value) >= 0.0005m
                    || verified.MaterialStatus != 9)
                    throw new InvalidOperationException($"MK response returned, but final material-position verification is not exact. Current consumed {verified.ConsumedKg:0.###} kg, status {verified.MaterialStatus} {verified.MaterialStatusText}. Do not rebook blindly.");
                tx.Status = TransactionStatuses.Success;
                await SaveEventAsync(tx, "SUCCESS", $"FA {request.OrderNo}, material position {request.MaterialPosition}: consumed quantity verified at {verified.ConsumedKg:0.###} kg, status 9.", CancellationToken.None);
            }
            catch (ProcessConflictException ex) { tx.Status = TransactionStatuses.Conflict; await SaveEventAsync(tx, "CONFLICT", ex.Message, ct); }
            catch (OxaionRejectedException ex) { tx.Status = TransactionStatuses.Rejected; await SaveEventAsync(tx, "REJECTED", ex.Message, ct); }
            catch (OxaionTransportException ex) { tx.Status = TransactionStatuses.Uncertain; await SaveEventAsync(tx, "UNCERTAIN", ex.Message, ct); }
            catch (Exception ex) { tx.Status = TransactionStatuses.ManualReviewRequired; await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct); }
            return tx;
        }
        finally { gate.Release(); }
    }

    public async Task<SeparateOperation> ReconcileAsync(string id, CancellationToken ct)
    {
        var tx = await _store.GetAsync(Kind, id, ct) ?? throw new KeyNotFoundException();
        if (tx.Status == TransactionStatuses.Success) return tx;
        var request = tx.ReadRequest<FaConsumptionRequest>();
        try
        {
            var current = await _materials.FindUniqueAsync(request.OrderNo, request.Article, ct);
            var matchesExpectedState = tx.TargetFaConsumedKg is not null
                && current.MaterialPosition == request.MaterialPosition
                && Math.Abs(current.ConsumedKg - tx.TargetFaConsumedKg.Value) < 0.0005m
                && current.MaterialStatus == 9;
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", matchesExpectedState
                ? $"Der aktuelle Oxaion-Zustand passt zur angeforderten MK-Buchung (Pos. {current.MaterialPosition}, gebucht {current.ConsumedKg:0.###} kg, Status 9), beweist ohne eindeutige Oxaion-Transaktionsreferenz aber nicht sicher, dass genau dieser WebApp-Vorgang die Änderung erzeugt hat. Nicht erneut buchen; manuell prüfen."
                : $"FA-Materialposition beweist den angeforderten MK-Ausgang nicht. Aktuell Pos. {current.MaterialPosition}, gebucht {current.ConsumedKg:0.###} kg, Status {current.MaterialStatus}. Nicht erneut buchen; manuell prüfen.", ct);
        }
        catch (Exception ex)
        {
            tx.Status = TransactionStatuses.ManualReviewRequired;
            await SaveEventAsync(tx, "MANUAL_REVIEW_REQUIRED", ex.Message, ct);
        }
        return tx;
    }

    private async Task SubmitMkAsync(OxaionSession session, FaConsumptionRequest r, FaMaterialPositionResult current, decimal targetTotal, CancellationToken ct)
    {
        var pos = r.MaterialPosition.ToString(CultureInfo.InvariantCulture);
        var penu = PersonnelService.ToOxaionPersonnelNumber(r.PersonnelNo);
        var objectKey = r.OrderNo + r.MaterialPosition.ToString("00000", CultureInfo.InvariantCulture) + r.Article;
        var load = await session.CallAsync("PW22000J", "*LOADNEW", Dict(
            ("STTXOA", "FAUNPOSN"), ("AMPOSN", pos), ("PCDPOSI", pos), ("PCBGNR", r.OrderNo),
            ("STTOBI", objectKey), ("AMIDNK", r.Article), ("PCANWG", "PPS"), ("KEYTYPE", "PWAMA"),
            ("STANWG", "PPS"), ("STORNO", "N"), ("AMFAUN", r.OrderNo), ("ARAKKZ", "MK")), ct);
        OxaionSession.AssertNoFcod(load);

        var chkInput = Merge(load.Dta, Dict(
            ("ARAKKZ", "MK"), ("TX_AKKZ", "Materialkomplettentnahme"), ("AKKZBZ", "Materialkomplettentnahme"),
            ("ARFAUN", r.OrderNo), ("ARPOSN", pos), ("ARPENU", penu),
            ("AMFAUN", r.OrderNo), ("AMPOSN", pos), ("AMIDNK", r.Article), ("AMFIRM", _options.Firm)));
        var chk = await session.CallAsync("PW22000J", "*CHK", chkInput, ct);
        OxaionSession.AssertNoFcod(chk);

        var loaded = await session.CallAsync("PW22031J", "*LOAD", Dict(("NOHWPgm", "PW22031"), ("NoHints", "")), ct);
        OxaionSession.AssertNoFcod(loaded);
        var newInput = Merge(loaded.Dta, chk.Dta);
        foreach (var x in Dict(("ARAKKZ", "MK"), ("TX_AKKZ", "Materialkomplettentnahme"), ("ARFAUN", r.OrderNo),
                     ("ARPOSN", pos), ("ARPENU", penu), ("ARFIRM", _options.Firm), ("ARIDNK", r.Article))) newInput[x.Key] = x.Value;
        var created = await session.CallAsync("PW22031J", "*NEW", newInput, ct);
        OxaionSession.AssertNoFcod(created);
        var state = Merge(newInput, created.Dta);
        foreach (var x in Dict(
                     ("ARAKKZ", "MK"), ("TX_AKKZ", "Materialkomplettentnahme"), ("ARFAUN", r.OrderNo),
                     ("ARPOSN", pos), ("ARPENU", penu), ("ARFIRM", _options.Firm), ("ARIDNK", r.Article),
                     ("I_ARIDNK", r.Article), ("I_TX_IDNK", r.Article), ("ARLAGO", r.TankWarehouse),
                     ("TX_LAGO", r.TankWarehouseText), ("ARLAPL", ""), ("ARPONR", r.TankBatch),
                     ("ARVBME2", FormatQty(targetTotal)), ("ARBGDT", DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                     ("ARVBMK", "KGM"), ("ARMEKZ", "KGM"), ("TX_INKT02", r.ArticleText), ("TX_IDNK02", r.ArticleText))) state[x.Key] = x.Value;

        var sn = await session.CallAsync("PW22031J", "*SNPFLICHT", state, ct);
        OxaionSession.AssertNoFcod(sn);
        var put = await session.CallAsync("PW22031J", "*PUTNEW", state, ct);
        OxaionSession.AssertNoFcod(put);
    }

    private static void Validate(FaConsumptionRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientOperationId) || string.IsNullOrWhiteSpace(r.PersonnelNo)
            || string.IsNullOrWhiteSpace(r.TankWarehouse) || string.IsNullOrWhiteSpace(r.Article)
            || string.IsNullOrWhiteSpace(r.TankBatch) || string.IsNullOrWhiteSpace(r.OrderNo) || r.MaterialPosition <= 0)
            throw new ArgumentException("Operation, personnel, tank, article/batch, FA and material position are required.");
        if (r.TankQuantityKg <= 0 || r.AdditionalConsumptionKg <= 0) throw new ArgumentException("Tank quantity and additional consumption must be > 0.");
        if (r.AdditionalConsumptionKg > r.TankQuantityKg + 0.0005m) throw new ArgumentException("Additional consumption exceeds prepared tank stock.");
        if (!FaMaterialService.MkStatusAllowed(r.ExpectedMaterialStatus))
            throw new ArgumentException("Normal MK booking is allowed only for prepared material status 0, 1 or 8. MU is intentionally not implemented yet.");
    }

    private async Task SaveEventAsync(SeparateOperation tx, string stage, string message, CancellationToken ct)
    { tx.Stage = stage; tx.Message = message; tx.Events.Add(new TransactionEvent(DateTimeOffset.UtcNow, stage, message)); await _store.SaveAsync(tx, ct); }
    private static Dictionary<string, string> Dict(params (string Key, string Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.Ordinal);
    private static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    { var r = new Dictionary<string, string>(a, StringComparer.Ordinal); foreach (var x in b) r[x.Key] = x.Value ?? ""; return r; }
    private static string FormatQty(decimal v) => v.ToString("0.000", CultureInfo.GetCultureInfo("de-AT"));
}

public sealed class InventoryService
{
    private readonly OxaionClient _oxaion;
    private readonly OxaionOptions _options;
    private readonly MachineStockService _machineStock;

    public InventoryService(OxaionClient oxaion, IOptions<OxaionOptions> options, MachineStockService machineStock)
    { _oxaion = oxaion; _options = options.Value; _machineStock = machineStock; }

    public async Task<IReadOnlyList<InventoryPosition>> ReadRpStockAsync(CancellationToken ct)
    {
        await using var session = await _oxaion.ConnectAsync(ct);
        var articles = await ReadRpArticleIndexAsync(session, ct);
        var result = new List<InventoryPosition>();
        foreach (var article in articles)
            result.AddRange(await ReadArticleAsync(session, article.Article, article.ArticleText, ct));
        return result.Where(x => x.QuantityKg != 0m)
            .OrderBy(x => x.Article, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Warehouse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.StorageBin, StringComparer.Ordinal)
            .ThenBy(x => x.Batch, StringComparer.Ordinal)
            .ToList();
    }

    internal async Task<IReadOnlyList<(string Article, string ArticleText)>> ReadRpArticleIndexAsync(OxaionSession session, CancellationToken ct)
    {
        var command = await session.CallAsync("MN10209J", "*CHKCMD", Dict(("CHKCMD", "CF"), ("_father_", "CMDLINE")), ct);
        OxaionSession.AssertNoFcod(command);
        var ssid = Get(command.Dta, "SSID");
        if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("MN10209J *CHKCMD CF did not return SSID for LB30210R.");
        var list = await session.CallAsync("LB30210R", "*FIRSTLIST", Dict(
            ("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("MC-Modus", ""), ("mode", "replace")), ct);
        OxaionSession.AssertNoFcod(list);
        if (!list.Xml.Descendants("STOP").Any())
            throw new InvalidOperationException("LB30210R article index did not return STOP in the confirmed FIRSTLIST. Pagination is not inferred for this index.");
        return ParseRpArticleIndex(list.Xml);
    }

    internal static IReadOnlyList<(string Article, string ArticleText)> ParseRpArticleIndex(XDocument xml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in xml.Descendants("ROW"))
        {
            var key = row.Element("KEY");
            var article = key?.Element("POIDNR")?.Value.Trim() ?? "";
            if (!article.StartsWith("RP.", StringComparison.OrdinalIgnoreCase)) continue;
            var text = row.Element("IDNR.TLBEZG")?.Value.Trim() ?? "";
            if (!result.ContainsKey(article)) result[article] = text;
        }
        return result.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => (x.Key, x.Value)).ToList();
    }

    private async Task<IReadOnlyList<InventoryPosition>> ReadArticleAsync(OxaionSession session, string article, string articleText, CancellationToken ct)
    {
        var warehouseRows = await ReadWarehouseRowsAsync(session, article, ct);
        var result = new List<InventoryPosition>();
        foreach (var warehouse in warehouseRows.Select(x => x.Warehouse).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var text = await ResolveWarehouseTextAsync(session, warehouse, ct);
            try
            {
                var positions = await ReadBinRowsAsync(session, article, warehouse, text, ct);
                result.AddRange(positions.Where(x => x.QuantityKg != 0m).Select(x => new InventoryPosition(
                    article, articleText, x.Warehouse, x.WarehouseText, x.StorageBin, x.Batch, x.QuantityKg, x.Unit, x.QuantityKg < 0m)));
            }
            catch (OxaionRejectedException ex) when (string.Equals(ex.Code, "LAG1626", StringComparison.OrdinalIgnoreCase))
            {
                var stock = await _machineStock.ReadAsync(session, warehouse, article, text, articleText, ct);
                result.AddRange(stock.Rows
                    .Where(x => string.Equals(x.Article, article, StringComparison.OrdinalIgnoreCase) && x.QuantityKg != 0m)
                    .Select(x => new InventoryPosition(article, string.IsNullOrWhiteSpace(x.ArticleText) ? articleText : x.ArticleText,
                        warehouse, text, "", x.Batch, x.QuantityKg, x.Unit, x.QuantityKg < 0m)));
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<(string Warehouse, string Batch, decimal QuantityKg)>> ReadWarehouseRowsAsync(OxaionSession session, string article, CancellationToken ct)
    {
        var context = BuildInquiryContext(article, "", "", "CL", "Chargen und Lagerorte pro Artikel", "LB30340R");
        var ssid = await LaunchAsync(session, context, "LB30340R", "LB30340", ct);
        var pages = await ReadAllPagesAsync(session, "LB30340R", ssid, ct);
        return pages.SelectMany(SourceStockService.ParseArticleWarehouseRows)
            .Where(r => string.Equals(r.Article, article, StringComparison.OrdinalIgnoreCase))
            .Select(r => (r.Warehouse, r.Batch, r.QuantityKg)).ToList();
    }

    private async Task<IReadOnlyList<SourceStockPosition>> ReadBinRowsAsync(OxaionSession session, string article, string warehouse, string text, CancellationToken ct)
    {
        var context = BuildInquiryContext(article, warehouse, text, "PT", "Lagerplätze pro Artikel und -ort", "LB30430R");
        var ssid = await LaunchAsync(session, context, "LB30430R", "LB30430", ct);
        var pages = await ReadAllPagesAsync(session, "LB30430R", ssid, ct);
        return pages.SelectMany(x => SourceStockService.ParsePositionRows(x, text))
            .Where(x => string.Equals(x.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task<string> LaunchAsync(OxaionSession session, Dictionary<string, string> context, string program, string noHw, CancellationToken ct)
    {
        var launch = await session.CallAsync("US30600J", "", context, ct); OxaionSession.AssertNoFcod(launch);
        var ssid = Get(launch.Dta, "SSID"); if (string.IsNullOrWhiteSpace(ssid)) throw new InvalidOperationException("US30600J did not return SSID for " + program);
        var hdr = Merge(context, launch.Dta); hdr["SSID"] = ssid; hdr["NOHWPgm"] = noHw;
        OxaionSession.AssertNoFcod(await session.CallAsync(program, "*GETHDR", hdr, ct));
        return ssid;
    }

    private static async Task<IReadOnlyList<XDocument>> ReadAllPagesAsync(OxaionSession session, string program, string ssid, CancellationToken ct)
    {
        var pages = new List<XDocument>();
        var page = await session.CallAsync(program, "*FIRSTLIST", Dict(("FLD", ""), ("SSID", ssid), ("PFLD", ""), ("MC-Modus", ""), ("mode", "reset")), ct);
        OxaionSession.AssertNoFcod(page);
        for (var i = 0; i < 100; i++)
        {
            pages.Add(page.Xml); if (MachineStockService.HasStop(page.Xml)) return pages;
            page = await session.CallAsync(program, "*NEXTLIST", Dict(("SSID", ssid)), ct); OxaionSession.AssertNoFcod(page);
        }
        throw new InvalidOperationException(program + " did not return STOP within 100 pages.");
    }

    private async Task<string> ResolveWarehouseTextAsync(OxaionSession session, string warehouse, CancellationToken ct)
    {
        var plain = await session.CallAsync("US00006J", "*GETPLAIN", Dict(("MFLD", "LAGO"), ("PGMN", "US30600J"),
            ("LAGO", warehouse), ("PFIELD", "TX_LAGO"), ("FIELD", "LAGO")), ct);
        OxaionSession.AssertNoFcod(plain);
        var text = Get(plain.Dta, "TX_LAGO"); return string.IsNullOrWhiteSpace(text) ? warehouse : text;
    }

    private Dictionary<string, string> BuildInquiryContext(string article, string warehouse, string warehouseText, string format, string formatText, string program) =>
        new(StringComparer.Ordinal)
        {
            ["WRKB"]="", ["DATV"]="", ["LHKZ20"]="", ["KSTB"]="", ["TIDF"]=format=="PT"?article:"", ["BWKZBZ"]="", ["NANW"]="", ["TSAKZ"]="",
            ["XLFTBZ"]="", ["mode"]="no-attribute-update", ["SNNR20"]="", ["TX_FFMT"]=formatText, ["TX_LAGR"]="", ["LAGR20"]="", ["KOBN"]="", ["TX_LAGO"]=warehouseText,
            ["DATB"]="", ["KOKO"]="0", ["PONR"]="", ["PONR20"]="", ["ABCK"]="", ["LHKZBZ"]="", ["LHKZ"]="", ["ABCK20"]="", ["TX_KSTT"]="", ["REPORT"]="",
            ["KSTTV"]="", ["ANWG"]="LBS", ["KOAW"]="", ["TX_BUKR"]="", ["INBR"]="", ["FMANWG"]="LBS", ["BGNR"]="", ["LHMT20"]="", ["XLFT"]="", ["BWKZ"]="",
            ["B_BBL20"]="", ["FFMT"]=format, ["LAGO20"]="", ["FFMS"]=format, ["LAGR"]="", ["KOPS"]="0", ["LAPL20"]="", ["PGMN"]=program, ["LAGO"]=warehouse,
            ["KSTTB"]="", ["TX_TIDF"]="", ["SNNR"]="", ["KOVU20"]="", ["BKFM"]="", ["LAPL"]="", ["KEYTYPE"]="P", ["BWKZ20"]="", ["TX_WERK"]="", ["I_TIDF"]=article,
            ["ABCKBZ"]="", ["XLFT20"]="", ["WRKV"]="", ["LHMT"]="", ["KSTV"]="", ["BUKR"]="", ["SSID"]="", ["LHMTBZ"]="", ["BLNR20"]="", ["DATE20"]="", ["BLNR"]="0",
            ["STARTUP"]=$"<DUFIRM>{_options.Firm}</DUFIRM><DUIDNV>{article}</DUIDNV><DULAGV>{warehouse}</DULAGV>", ["NEXTPGM"]=program
        };

    private static Dictionary<string,string> Dict(params (string Key,string Value)[] values)=>values.ToDictionary(x=>x.Key,x=>x.Value??"",StringComparer.Ordinal);
    private static Dictionary<string,string> Merge(IReadOnlyDictionary<string,string>a,IReadOnlyDictionary<string,string>b){var r=new Dictionary<string,string>(a,StringComparer.Ordinal);foreach(var x in b)r[x.Key]=x.Value??"";return r;}
    private static string Get(IReadOnlyDictionary<string,string>d,string k)=>d.TryGetValue(k,out var v)?v:"";
}

public static class SeparateProcessFeatureExtensions
{
    public static IServiceCollection AddSeparateProcessFeatures(this IServiceCollection services)
    {
        services.AddSingleton<SeparateOperationStore>();
        services.AddSingleton<MaterialTransferBookingService>();
        services.AddSingleton<TankOutService>();
        services.AddSingleton<FillNewService>();
        services.AddSingleton<FaMaterialService>();
        services.AddSingleton<FaConsumptionService>();
        services.AddSingleton<InventoryService>();
        return services;
    }

    public static IEndpointRouteBuilder MapSeparateProcessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/article-recognition-colors", async (string article, HttpContext http, OxaionClient oxaion, CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try
            {
                await using var session = await oxaion.ConnectAsync(ct);
                return Results.Ok(await ArticleRecognitionColorLookup.ReadAsync(session, article, ct));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapGet("/api/fa-material", async (string orderNo, string article, HttpContext http, FaMaterialService service, CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.FindUniqueAsync(orderNo, article, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapGet("/api/inventory/rp-stock", async (HttpContext http, InventoryService service, CancellationToken ct) =>
        {
            if (!SessionAuthenticated(http, out var auth)) return auth!;
            try { return Results.Ok(await service.ReadRpStockAsync(ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(ex.Message, statusCode:503); }
        });

        endpoints.MapPost("/api/tank-out", async (TankOutRequest request, HttpContext http, TankOutService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/tank-out/{id}", async (string id, HttpContext http, TankOutService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound(); });
        endpoints.MapPost("/api/tank-out/{id}/reconcile", async (string id, HttpContext http, TankOutService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; try { return OperationResult(await service.ReconcileAsync(id, ct)); } catch (KeyNotFoundException) { return Results.NotFound(); } });

        endpoints.MapPost("/api/fill-new", async (FillNewRequest request, HttpContext http, FillNewService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/fill-new/{id}", async (string id, HttpContext http, FillNewService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound(); });
        endpoints.MapPost("/api/fill-new/{id}/reconcile", async (string id, HttpContext http, FillNewService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; try { return OperationResult(await service.ReconcileAsync(id, ct)); } catch (KeyNotFoundException) { return Results.NotFound(); } });

        endpoints.MapPost("/api/fa-consumption", async (FaConsumptionRequest request, HttpContext http, FaConsumptionService service, CancellationToken ct) =>
        {
            if (!SessionMatches(http, request, out var auth)) return auth!;
            try { return OperationResult(await service.ExecuteAsync(request, ct)); }
            catch (ProcessConflictException ex) { return Results.Json(new { status="CONFLICT", message=ex.Message }, statusCode:409); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error=ex.Message }); }
        });
        endpoints.MapGet("/api/fa-consumption/{id}", async (string id, HttpContext http, FaConsumptionService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; return await service.GetAsync(id, ct) is { } tx ? Results.Ok(tx.ToResponse()) : Results.NotFound(); });
        endpoints.MapPost("/api/fa-consumption/{id}/reconcile", async (string id, HttpContext http, FaConsumptionService service, CancellationToken ct) =>
        { if (!SessionAuthenticated(http, out var auth)) return auth!; try { return OperationResult(await service.ReconcileAsync(id, ct)); } catch (KeyNotFoundException) { return Results.NotFound(); } });

        return endpoints;
    }

    private static bool SessionAuthenticated(HttpContext http, out IResult? result)
    {
        var no = http.Session.GetString(PersonnelAuthenticationSession.PersonnelNo);
        var name = http.Session.GetString(PersonnelAuthenticationSession.PersonnelName);
        if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(name))
        {
            result = Results.Json(new { status="AUTH_REQUIRED", message="Bitte Mitarbeiter anmelden." }, statusCode:401);
            return false;
        }
        result = null;
        return true;
    }

    private static bool SessionMatches(HttpContext http, ISeparatePersonnelRequest request, out IResult? result)
    {
        var no = http.Session.GetString(PersonnelAuthenticationSession.PersonnelNo);
        var name = http.Session.GetString(PersonnelAuthenticationSession.PersonnelName);
        if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(name))
        {
            result = Results.Json(new { status="AUTH_REQUIRED", stage="PERSONNEL_VALIDATION", message="Bitte Mitarbeiter anmelden. Es wurde keine Materialbuchung gestartet." }, statusCode:401);
            return false;
        }
        if (!string.Equals(no, request.PersonnelNo, StringComparison.Ordinal) || !string.Equals(name, request.PersonnelName, StringComparison.Ordinal))
        {
            result = Results.Json(new { status="AUTH_CONFLICT", stage="PERSONNEL_VALIDATION", message="Angemeldeter Mitarbeiter stimmt nicht mit dem Vorgang überein. Es wurde keine Materialbuchung gestartet." }, statusCode:403);
            return false;
        }
        result = null; return true;
    }

    private static IResult OperationResult(SeparateOperation tx) => tx.Status switch
    {
        TransactionStatuses.Success => Results.Ok(tx.ToResponse()),
        TransactionStatuses.Rejected => Results.Json(tx.ToResponse(), statusCode:422),
        TransactionStatuses.Conflict or TransactionStatuses.Uncertain or TransactionStatuses.ManualReviewRequired => Results.Json(tx.ToResponse(), statusCode:409),
        _ => Results.Accepted(value: tx.ToResponse())
    };
}
