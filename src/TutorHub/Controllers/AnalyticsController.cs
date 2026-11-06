using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Controllers;

[Authorize(Policy = AppPolicies.Staff)]
public class AnalyticsController(FeatureFlags flags, LearningAnalytics analytics) : AppController
{
    public async Task<IActionResult> Index() =>
        await flags.IsEnabledAsync(AppFeature.Analytics)
            ? View(await analytics.BuildAsync())
            : View("Upgrade");
}
