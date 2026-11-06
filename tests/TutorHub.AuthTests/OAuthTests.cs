using System.Net;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class OAuthTests(AuthTestFactory factory)
{
    private static async Task<HttpResponseMessage> StartGoogleAsync(HttpClient browser, string email, string key)
    {
        FakeGoogleHandler.Email = email;
        FakeGoogleHandler.ProviderKey = key;

        var challenge = await browser.PostFormAsync("/Account/ExternalLogin?provider=Google", "/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        return await browser.GetAsync(challenge.Headers.Location);
    }

    private static Task<HttpResponseMessage> CompleteRegistrationAsync(HttpClient browser, string? workspace = null, string? invite = null)
    {
        var fields = new Dictionary<string, string> { ["FullName"] = "Google User" };
        if (workspace is not null)
        {
            fields["WorkspaceName"] = workspace;
        }

        if (invite is not null)
        {
            fields["InviteCode"] = invite;
        }

        return PostWithTokenFromResponseAsync(browser, fields);
    }

    private static async Task<HttpResponseMessage> PostWithTokenFromResponseAsync(HttpClient browser, Dictionary<string, string> fields)
    {
        var html = await browser.GetStringAsync("/Account/Login");
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        fields["__RequestVerificationToken"] = token;
        return await browser.PostAsync("/Account/ExternalRegister", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Login_page_offers_the_configured_provider()
    {
        var html = await factory.Anonymous().GetStringAsync("/Account/Login");
        Assert.Contains("Увійти через Google", html);
    }

    [Fact]
    public async Task First_google_login_asks_for_workspace_then_creates_an_owner()
    {
        var browser = factory.Browser();
        var email = $"{AuthTestFactory.Unique("google")}@example.com";

        var callback = await StartGoogleAsync(browser, email, AuthTestFactory.Unique("key"));

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Contains(email, await callback.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Tutors")).StatusCode);

        var completed = await CompleteRegistrationAsync(browser, workspace: AuthTestFactory.Unique("workspace"));

        Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Tutors")).StatusCode);
    }

    [Fact]
    public async Task Returning_google_user_signs_in_directly()
    {
        var email = $"{AuthTestFactory.Unique("google")}@example.com";
        var key = AuthTestFactory.Unique("key");

        var first = factory.Browser();
        await StartGoogleAsync(first, email, key);
        await CompleteRegistrationAsync(first, workspace: AuthTestFactory.Unique("workspace"));

        var second = factory.Browser();
        var callback = await StartGoogleAsync(second, email, key);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/Tutors")).StatusCode);
    }

    [Fact]
    public async Task Google_account_does_not_take_over_an_existing_email()
    {
        var existing = await factory.RegisterAsync();
        var browser = factory.Browser();

        var callback = await StartGoogleAsync(browser, existing.Email, AuthTestFactory.Unique("key"));

        var page = await browser.FollowRedirectAsync(callback);
        Assert.Contains("уже існує", page);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Tutors")).StatusCode);
    }

    [Fact]
    public async Task Google_user_can_join_with_a_student_invite()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync("Own lesson");
        var invite = await owner.Client.PostAsync($"/api/students/{graph.StudentId}/invite", null);
        var code = (await invite.ReadJsonAsync()).GetProperty("inviteCode").GetString()!;

        var browser = factory.Browser();
        await StartGoogleAsync(browser, $"{AuthTestFactory.Unique("google")}@example.com", AuthTestFactory.Unique("key"));
        await CompleteRegistrationAsync(browser, invite: code);

        Assert.Contains("Own lesson", await browser.GetStringAsync("/Lessons"));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Tutors")).StatusCode);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Identity.Application")]
    [InlineData("Bearer")]
    public async Task Only_external_providers_can_be_challenged(string provider)
    {
        var browser = factory.Browser();

        var response = await browser.PostFormAsync($"/Account/ExternalLogin?provider={provider}", "/Account/Login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Provider_error_returns_to_login()
    {
        var browser = factory.Browser();

        var response = await browser.GetAsync("/Account/ExternalCallback?remoteError=access_denied");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_without_provider_data_returns_to_login()
    {
        var browser = factory.Browser();

        var response = await browser.GetAsync("/Account/ExternalCallback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task External_registration_without_provider_data_creates_nothing()
    {
        var browser = factory.Browser();

        var response = await CompleteRegistrationAsync(browser, workspace: "Forged");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Tutors")).StatusCode);
    }
}
