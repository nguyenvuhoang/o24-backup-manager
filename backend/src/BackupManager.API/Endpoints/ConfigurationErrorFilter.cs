using BackupManager.Application.Connections;

namespace BackupManager.API.Endpoints;

public sealed class ConfigurationErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (ConfigurationException ex)
        {
            var status = ex.Code switch
            {
                "NOT_FOUND" => 404, "CONNECTION_IN_USE" => 409,
                "CONNECTION_FAILED" or "DISCOVERY_FAILED" => 422, _ => 400
            };
            return Results.Json(new { success = false, message = ex.Message, errorCode = ex.Code, errors = ex.Errors }, statusCode: status);
        }
        catch (OperationCanceledException) { return Results.StatusCode(499); }
        catch (Exception)
        {
            // Do not serialize or log exceptions containing SQL credentials or filesystem details.
            return Results.Json(new { success = false, message = "Không xử lý được yêu cầu. Kiểm tra cấu hình MasterKey và quyền đọc/ghi của backend.", errorCode = "CONFIGURATION_FAILED" }, statusCode: 500);
        }
    }
}
