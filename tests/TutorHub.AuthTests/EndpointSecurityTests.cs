using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class EndpointSecurityTests(AuthTestFactory factory)
{
    private static readonly HashSet<string> AllowedAnonymous =
    [
        "Account.Login",
        "Account.Register",
        "Account.Logout",
        "Account.AccessDenied",
        "Account.ExternalLogin",
        "Account.ExternalCallback",
        "Account.ExternalRegister",
        "Home.Status",
        "Home.Error",
        "AuthApi.Register",
        "AuthApi.Token",
        "TutorProfile.Index",
        "TutorProfile.Photo"
    ];

    private List<(string Name, EndpointMetadataCollection Metadata)> ControllerActions() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(e => (Descriptor: e.Metadata.GetMetadata<ControllerActionDescriptor>(), e.Metadata))
            .Where(e => e.Descriptor is not null)
            .Select(e => ($"{e.Descriptor!.ControllerName}.{e.Descriptor.ActionName}", e.Metadata))
            .ToList();

    [Fact]
    public void Every_action_outside_the_allow_list_requires_authorization()
    {
        var actions = ControllerActions();
        Assert.NotEmpty(actions);

        var unprotected = actions
            .Where(a => a.Metadata.GetMetadata<IAllowAnonymous>() is not null || a.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(a => a.Name)
            .Where(name => !AllowedAnonymous.Contains(name))
            .Distinct()
            .ToList();

        Assert.Empty(unprotected);
    }

    [Theory]
    [InlineData("Tutors")]
    [InlineData("Students")]
    [InlineData("Subjects")]
    [InlineData("Plan")]
    [InlineData("Analytics")]
    [InlineData("TutorsApi")]
    [InlineData("StudentsApi")]
    [InlineData("SubjectsApi")]
    [InlineData("AnalyticsApi")]
    public void Staff_controllers_require_the_staff_policy(string controller)
    {
        var actions = ControllerActions().Where(a => a.Name.StartsWith($"{controller}.")).ToList();
        Assert.NotEmpty(actions);

        Assert.All(actions, a =>
            Assert.Contains(a.Metadata.GetOrderedMetadata<IAuthorizeData>(), d => d.Policy == "Staff"));
    }
}
