namespace Fam.Pulverentnahme.Web;

public static class ChargeOriginEndpoints
{
    public static IEndpointRouteBuilder MapChargeOriginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/charge-origin", async (
            string article,
            string batch,
            long? objectId,
            ChargeOriginService service,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.ReadAsync(article, batch, objectId, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new
                {
                    status = "CHARGE_ORIGIN_INVALID",
                    message = ex.Message
                });
            }
            catch (OxaionRejectedException ex)
            {
                return Results.Json(new
                {
                    status = "CHARGE_ORIGIN_REJECTED",
                    message = ex.Message
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (ChargeOriginProtocolException ex)
            {
                return Results.Json(new
                {
                    status = "CHARGE_ORIGIN_INCOMPLETE",
                    message = ex.Message
                }, statusCode: StatusCodes.Status502BadGateway);
            }
            catch (OxaionTransportException ex)
            {
                return Results.Json(new
                {
                    status = "CHARGE_ORIGIN_UNAVAILABLE",
                    message = "Chargenherkunft konnte nicht sicher aus Oxaion gelesen werden.",
                    technicalMessage = ex.Message
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new
                {
                    status = "CHARGE_ORIGIN_UNAVAILABLE",
                    message = "Chargenherkunft konnte nicht sicher aus Oxaion gelesen werden.",
                    technicalMessage = ex.Message
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return endpoints;
    }
}
