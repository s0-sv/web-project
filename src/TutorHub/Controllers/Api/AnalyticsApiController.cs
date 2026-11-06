using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorHub.Contracts;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers.Api;

[Authorize(Policy = AppPolicies.Staff)]
[Route("api/analytics")]
public class AnalyticsApiController(FeatureFlags flags, LearningAnalytics analytics) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AnalyticsDto>> Get(CancellationToken ct)
    {
        if (!await flags.IsEnabledAsync(AppFeature.Analytics, ct))
        {
            return Problem(
                detail: "This feature requires the Premium plan.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Feature not available");
        }

        return await analytics.BuildAsync(ct);
    }
}
