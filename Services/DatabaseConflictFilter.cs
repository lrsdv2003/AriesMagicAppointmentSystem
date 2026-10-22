using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

public sealed class DatabaseConflictFilter(ILogger<DatabaseConflictFilter> logger) : IExceptionFilter
{
    public static bool IsConflict(Exception error) => error is DbUpdateConcurrencyException
        || error is SqlException {Number:1205 or 3960 or 41302 or 41305 or 41325}
        || (error.InnerException != null && IsConflict(error.InnerException));
    public void OnException(ExceptionContext context)
    {
        if (!IsConflict(context.Exception)) return;
        logger.LogWarning("Database contention rolled back request {TraceId}.",context.HttpContext.TraceIdentifier);
        context.Result=new ObjectResult(new {error="Another request changed this record. Refresh its current status before trying again."}){StatusCode=409};
        context.ExceptionHandled=true;
    }
}
