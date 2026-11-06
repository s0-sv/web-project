using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TutorHub.Models;

namespace TutorHub.Controllers;

public class LoginForm
{
    [Display(Name = "Email")]
    [Required(ErrorMessage = "Вкажіть email")]
    [EmailAddress(ErrorMessage = "Некоректний email")]
    public string Email { get; set; } = "";

    [Display(Name = "Пароль")]
    [Required(ErrorMessage = "Вкажіть пароль")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public class RegisterForm
{
    [Display(Name = "ПІБ")]
    [Required(ErrorMessage = "Вкажіть ПІБ")]
    [StringLength(150, ErrorMessage = "ПІБ не може перевищувати 150 символів")]
    public string FullName { get; set; } = "";

    [Display(Name = "Email")]
    [Required(ErrorMessage = "Вкажіть email")]
    [EmailAddress(ErrorMessage = "Некоректний email")]
    [StringLength(254, ErrorMessage = "Email не може перевищувати 254 символи")]
    public string Email { get; set; } = "";

    [Display(Name = "Пароль")]
    [Required(ErrorMessage = "Вкажіть пароль")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Пароль має містити від 8 до 100 символів")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Повторіть пароль")]
    [Compare(nameof(Password), ErrorMessage = "Паролі не збігаються")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}

public class ExternalRegisterForm
{
    [Display(Name = "ПІБ")]
    [Required(ErrorMessage = "Вкажіть ПІБ")]
    [StringLength(150, ErrorMessage = "ПІБ не може перевищувати 150 символів")]
    public string FullName { get; set; } = "";

    public string? Email { get; set; }

    public string? Provider { get; set; }
}

[AllowAnonymous]
public class AccountController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ILogger<AccountController> logger) : AppController
{
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectLocal(returnUrl);
        }

        await PrepareLoginViewAsync(returnUrl);
        return View(new LoginForm());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginForm input, string? returnUrl = null)
    {
        await PrepareLoginViewAsync(returnUrl);

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var result = await signIn.PasswordSignInAsync(input.Email, input.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return RedirectLocal(returnUrl);
        }

        ModelState.AddModelError("", result.IsLockedOut
            ? "Забагато невдалих спроб. Спробуйте за 15 хвилин."
            : "Неправильний email або пароль.");
        return View(input);
    }

    public IActionResult Register() =>
        User.Identity?.IsAuthenticated == true ? RedirectLocal(null) : View(new RegisterForm());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterForm input)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var user = new ApplicationUser { UserName = input.Email, Email = input.Email, FullName = input.FullName };
        var result = await users.CreateAsync(user, input.Password);

        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(input);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        FlashSuccess("Обліковий запис створено.");
        return RedirectLocal(null);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied() => View();

    [HttpPost]
    public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl = null)
    {
        var schemes = await signIn.GetExternalAuthenticationSchemesAsync();
        if (schemes.All(s => s.Name != provider))
        {
            return NotFound();
        }

        var callback = Url.Action(nameof(ExternalCallback), new { returnUrl });
        return Challenge(signIn.ConfigureExternalAuthenticationProperties(provider, callback), provider);
    }

    public async Task<IActionResult> ExternalCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError is not null)
        {
            logger.LogWarning("External sign-in failed: {Error}", remoteError);
            FlashError("Не вдалося увійти через зовнішній сервіс.");
            return RedirectToAction(nameof(Login));
        }

        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null)
        {
            FlashError("Не вдалося отримати дані від зовнішнього сервісу.");
            return RedirectToAction(nameof(Login));
        }

        var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            return RedirectLocal(returnUrl);
        }

        if (result.IsLockedOut)
        {
            FlashError("Обліковий запис тимчасово заблоковано.");
            return RedirectToAction(nameof(Login));
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (email is null)
        {
            FlashError("Зовнішній сервіс не надав email.");
            return RedirectToAction(nameof(Login));
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            FlashError("Обліковий запис із цим email уже існує. Увійдіть за допомогою пароля.");
            return RedirectToAction(nameof(Login));
        }

        return View("ExternalRegister", new ExternalRegisterForm
        {
            FullName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "",
            Email = email,
            Provider = info.ProviderDisplayName
        });
    }

    [HttpPost]
    public async Task<IActionResult> ExternalRegister(ExternalRegisterForm input)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        var email = info?.Principal.FindFirstValue(ClaimTypes.Email);
        if (info is null || email is null)
        {
            FlashError("Сеанс входу завершився. Спробуйте ще раз.");
            return RedirectToAction(nameof(Login));
        }

        input.Email = email;
        input.Provider = info.ProviderDisplayName;

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var user = new ApplicationUser { UserName = email, Email = email, FullName = input.FullName };
        var result = await users.CreateAsync(user);

        if (result.Succeeded)
        {
            result = await users.AddLoginAsync(user, info);
        }

        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(input);
        }

        await signIn.SignInAsync(user, isPersistent: false, info.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        FlashSuccess("Обліковий запис створено.");
        return RedirectLocal(null);
    }

    private async Task PrepareLoginViewAsync(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.ExternalSchemes = (await signIn.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    private IActionResult RedirectLocal(string? returnUrl) =>
        returnUrl is not null && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("", error.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) =>
                    "Обліковий запис із цим email уже існує.",
                nameof(IdentityErrorDescriber.PasswordTooShort) => "Пароль занадто короткий.",
                nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "Пароль має містити хоча б одну цифру.",
                nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) =>
                    "Некоректний email.",
                _ => error.Description
            });
        }
    }
}
