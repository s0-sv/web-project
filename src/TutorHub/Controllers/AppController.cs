using Microsoft.AspNetCore.Mvc;

namespace TutorHub.Controllers;

[AutoValidateAntiforgeryToken]
public abstract class AppController : Controller
{
    protected void FlashSuccess(string message) => TempData["Success"] = message;

    protected void FlashError(string message) => TempData["Error"] = message;
}
