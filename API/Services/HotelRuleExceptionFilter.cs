using System.Data.Common;
using API.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Services;

// Turns a HotelRuleException into 400 (or 404) with { "message": "..." },
// which the client shows to the guest or staff member. Database failures become
// 503 with a friendly message; the details go to the server log only, never to
// the client (no SQL text, stack traces, or connection details).
public class HotelRuleExceptionFilter(ILogger<HotelRuleExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case HotelRuleException rule:
                var body = new { message = rule.Message };
                context.Result = rule.NotFound ? new NotFoundObjectResult(body) : new BadRequestObjectResult(body);
                break;
            case DbException or DatabaseUnavailableException:
                logger.LogError(context.Exception, "Database error on {Method} {Path}",
                    context.HttpContext.Request.Method, context.HttpContext.Request.Path);
                context.Result = new ObjectResult(new { message = "Our reservation system is offline for a moment. Please try again shortly." })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
                break;
            default:
                return;
        }
        context.ExceptionHandled = true;
    }
}
