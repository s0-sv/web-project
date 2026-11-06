using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;

namespace TutorHub.Controllers;

[AllowAnonymous]
public class SeoController(AppDbContext db, IConfiguration config) : AppController
{
    public static string BaseUrl(IConfiguration config, HttpRequest request) =>
        (config["Seo:BaseUrl"] ?? $"{request.Scheme}://{request.Host}").TrimEnd('/');

    [HttpGet("/robots.txt")]
    public ContentResult Robots()
    {
        var body = $"""
            User-agent: *
            Allow: /tutor/
            Allow: /lib/
            Allow: /css/
            Allow: /js/
            Disallow: /

            Sitemap: {BaseUrl(config, Request)}/sitemap.xml

            """;

        return Content(body, "text/plain");
    }

    [HttpGet("/sitemap.xml")]
    public async Task<ContentResult> Sitemap()
    {
        var slugs = await db.Tutors.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.IsPublished && t.Slug != null)
            .OrderBy(t => t.Slug)
            .Select(t => t.Slug!)
            .ToListAsync();

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var baseUrl = BaseUrl(config, Request);
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "urlset", slugs.Select(slug =>
                new XElement(ns + "url", new XElement(ns + "loc", $"{baseUrl}/tutor/{slug}")))));

        return Content(document.Declaration + Environment.NewLine + document, "application/xml");
    }
}
