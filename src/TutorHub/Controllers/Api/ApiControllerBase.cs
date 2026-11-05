using Microsoft.AspNetCore.Mvc;

namespace TutorHub.Controllers.Api;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult ConflictProblem(string detail) =>
        Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    protected ActionResult InvalidField(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
