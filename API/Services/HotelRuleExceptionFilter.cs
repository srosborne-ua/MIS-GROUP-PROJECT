using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Services;

// Turns a HotelRuleException into 400 (or 404) with { "message": "..." },
// which the client shows to the guest or staff member.
public class HotelRuleExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not HotelRuleException rule) return;
        var body = new { message = rule.Message };
        context.Result = rule.NotFound ? new NotFoundObjectResult(body) : new BadRequestObjectResult(body);
        context.ExceptionHandled = true;
    }
}
