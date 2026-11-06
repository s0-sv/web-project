using System.Net;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class MvcAuthTests(AuthTestFactory factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Tutors")]
    [InlineData("/Students")]
    [InlineData("/Subjects")]
    [InlineData("/Lessons")]
    [InlineData("/Lessons/Details/1")]
    [InlineData("/Materials")]
    [InlineData("/Materials/Download/1")]
    [InlineData("/Plan")]
    [InlineData("/Analytics")]
    public async Task Anonymous_page_redirects_to_login(string path)
    {
        var response = await factory.Anonymous().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/js/site.js")]
    public async Task Public_resources_are_available(string path)
    {
        var response = await factory.Anonymous().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Registration_creates_an_owner_and_signs_in()
    {
        var browser = factory.Browser();

        var response = await browser.PostFormAsync("/Account/Register", "/Account/Register", new()
        {
            ["FullName"] = "New Owner",
            ["Email"] = $"{AuthTestFactory.Unique("owner")}@example.com",
            ["Password"] = "Test-password-1",
            ["ConfirmPassword"] = "Test-password-1",
            ["WorkspaceName"] = AuthTestFactory.Unique("workspace")
        });

        var home = await browser.FollowRedirectAsync(response);
        Assert.Contains("Найближчі заняття", home);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Plan")).StatusCode);
    }

    [Fact]
    public async Task Registration_shows_validation_errors()
    {
        var browser = factory.Browser();

        var response = await browser.PostFormAsync("/Account/Register", "/Account/Register", new()
        {
            ["FullName"] = "New Owner",
            ["Email"] = $"{AuthTestFactory.Unique("owner")}@example.com",
            ["Password"] = "Test-password-1",
            ["ConfirmPassword"] = "Different-password-1",
            ["WorkspaceName"] = "Workspace"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Паролі не збігаються", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registration_without_workspace_or_invite_shows_error()
    {
        var browser = factory.Browser();

        var response = await browser.PostFormAsync("/Account/Register", "/Account/Register", new()
        {
            ["FullName"] = "New Owner",
            ["Email"] = $"{AuthTestFactory.Unique("owner")}@example.com",
            ["Password"] = "Test-password-1",
            ["ConfirmPassword"] = "Test-password-1"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("робочого простору або код запрошення", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_with_wrong_password_shows_error_and_stays_anonymous()
    {
        var user = await factory.RegisterAsync();
        var browser = factory.Browser();

        var response = await browser.PostFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = user.Email,
            ["Password"] = "Wrong-password-1"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Неправильний email або пароль", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Tutors")).StatusCode);
    }

    [Fact]
    public async Task Login_locks_after_five_failures()
    {
        var user = await factory.RegisterAsync();
        var browser = factory.Browser();
        var fields = new Dictionary<string, string> { ["Email"] = user.Email, ["Password"] = "Wrong-password-1" };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await browser.PostFormAsync("/Account/Login", "/Account/Login", fields);
        }

        var response = await browser.PostFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = user.Email,
            ["Password"] = user.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Забагато невдалих спроб", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_returns_to_a_local_url()
    {
        var user = await factory.RegisterAsync();
        var browser = factory.Browser();

        var response = await browser.PostFormAsync("/Account/Login?returnUrl=%2FTutors", "/Account/Login", new()
        {
            ["Email"] = user.Email,
            ["Password"] = user.Password
        });

        Assert.Equal("/Tutors", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("//evil.example.com/")]
    public async Task Login_ignores_an_external_return_url(string target)
    {
        var user = await factory.RegisterAsync();
        var browser = factory.Browser();

        var response = await browser.PostFormAsync($"/Account/Login?returnUrl={Uri.EscapeDataString(target)}", "/Account/Login", new()
        {
            ["Email"] = user.Email,
            ["Password"] = user.Password
        });

        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var user = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(user);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Tutors")).StatusCode);

        var response = await browser.PostFormAsync("/Account/Logout", "/Tutors");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Tutors")).StatusCode);
    }

    [Fact]
    public async Task Session_cookie_is_http_only()
    {
        var user = await factory.RegisterAsync();
        var browser = factory.Browser();

        var response = await browser.PostFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = user.Email,
            ["Password"] = user.Password
        });

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Identity.Application"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        var user = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(user);

        var response = await browser.PostAsync("/Subjects/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "NoToken" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_ignores_the_session_cookie()
    {
        var user = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(user);

        var response = await browser.GetAsync("/api/subjects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pages_ignore_the_bearer_token()
    {
        var user = await factory.RegisterAsync();

        var response = await user.Client.GetAsync("/Tutors");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Tutors")]
    [InlineData("/Tutors/Create")]
    [InlineData("/Students")]
    [InlineData("/Students/Create")]
    [InlineData("/Subjects")]
    [InlineData("/Subjects/Create")]
    [InlineData("/Lessons")]
    [InlineData("/Lessons/Create")]
    [InlineData("/Materials")]
    [InlineData("/Plan")]
    [InlineData("/Analytics")]
    public async Task Pages_render_for_a_signed_in_owner(string path)
    {
        var user = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(user);

        var response = await browser.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Owner_can_use_crud_pages_inside_their_tenant()
    {
        var user = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(user);
        var name = AuthTestFactory.Unique("Предмет");

        var created = await browser.PostFormAsync("/Subjects/Create", "/Subjects/Create", new() { ["Name"] = name });
        var details = await browser.FollowRedirectAsync(created);
        Assert.Contains(name, details);

        var duplicate = await browser.PostFormAsync("/Subjects/Create", "/Subjects/Create", new() { ["Name"] = name });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("вже існує", await duplicate.Content.ReadAsStringAsync());
    }
}
