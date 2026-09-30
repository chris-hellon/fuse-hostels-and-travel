using System.Globalization;
using System.Text;
using System.Xml;
using Mediator;
using Microsoft.AspNetCore.Rewrite;
using Fuse.Contracts.Site;

namespace Fuse.Web.Api;

/// <summary>
/// What legacy's host did for search engines: robots.txt, sitemap.xml, the SEO redirects from Page Management
/// (permanent, before anything else) and www → the bare domain.
/// </summary>
public static class SiteSeoEndpoints
{
    /// <summary>Requests that are never a page someone could have linked to, so never redirected.</summary>
    private static readonly string[] NotPages = ["/_framework", "/_content", "/_blazor", "/api", "/admin", "/uploads", "/dev"];

    /// <summary>
    /// Addresses legacy's sitemap listed that never worked (search engines may still ask): the guides lived at
    /// /blogs-and-tips, and the contact page at /contact. Anything under the old address moves across.
    /// </summary>
    private static readonly (string Old, string New)[] MovedSections = [("/travel-guides", "/blogs-and-tips"), ("/get-in-touch", "/contact")];

    /// <summary>www → the bare domain, then the SEO redirects. Before routing, as legacy.</summary>
    public static void UseSiteRedirects(this WebApplication app)
    {
        app.UseRewriter(new RewriteOptions().AddRedirectToNonWwwPermanent());
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                && !NotPages.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                // Page management's redirects first (they name exact pages), then whole moved sections.
                var sender = context.RequestServices.GetRequiredService<ISender>();
                var requested = (path.Value ?? "").TrimStart('/');
                var redirect = (await sender.Send(new ListSiteRedirects(), context.RequestAborted))
                    .FirstOrDefault(r => string.Equals(r.Url.TrimStart('/'), requested, StringComparison.OrdinalIgnoreCase));
                if (redirect is not null)
                {
                    context.Response.Redirect(redirect.RedirectUrl, permanent: true);
                    return;
                }
                foreach (var (from, to) in MovedSections)
                {
                    if (path.StartsWithSegments(from, StringComparison.OrdinalIgnoreCase, out var rest))
                    {
                        context.Response.Redirect($"{to}{rest}{context.Request.QueryString}", permanent: true);
                        return;
                    }
                }
            }
            await next();
        });
    }

    public static void MapSiteSeo(this IEndpointRouteBuilder app)
    {
        // Outside production nothing is to be indexed (legacy did the same for its staging host).
        app.MapGet("/robots.txt", (HttpRequest request, IHostEnvironment environment) =>
                Results.Text(new StringBuilder()
                    .Append("User-agent: *\n")
                    .Append(environment.IsProduction() ? "Disallow:\n" : "Disallow: /\n")
                    .Append($"Sitemap: {request.Scheme}://{request.Host}/sitemap.xml\n")
                    .ToString(), "text/plain", Encoding.UTF8))
            .AllowAnonymous();

        app.MapGet("/sitemap.xml", async (HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var entries = await sender.Send(new GetSiteMap(), cancellationToken);
                return Results.Text(Write(entries, $"{request.Scheme}://{request.Host}"), "application/xml", Encoding.UTF8);
            })
            .AllowAnonymous();
    }

    private const string Sitemaps = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string Videos = "http://www.google.com/schemas/sitemap-video/1.1";
    private const string News = "http://www.google.com/schemas/sitemap-news/0.9";

    /// <summary>Legacy's (SimpleMvcSitemap) format, dates in Vietnam time.</summary>
    private static string Write(IReadOnlyList<SiteMapEntry> entries, string baseUrl)
    {
        var text = new StringWriterUtf8();
        using (var xml = XmlWriter.Create(text, new XmlWriterSettings { Encoding = Encoding.UTF8 }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", Sitemaps);
            xml.WriteAttributeString("xmlns", "news", null, News);
            xml.WriteAttributeString("xmlns", "video", null, Videos);
            foreach (var entry in entries)
            {
                xml.WriteStartElement("url", Sitemaps);
                xml.WriteElementString("loc", Sitemaps, entry.Path.Length == 0 ? baseUrl : $"{baseUrl}/{entry.Path}");
                if (entry.LastModified is { } modified)
                    xml.WriteElementString("lastmod", Sitemaps, Date(modified));
                xml.WriteElementString("changefreq", Sitemaps, entry.ChangeFrequency);
                xml.WriteElementString("priority", Sitemaps, entry.Priority.ToString("0.0", CultureInfo.InvariantCulture));
                if (entry.Video is { } video)
                {
                    xml.WriteStartElement("video", "video", Videos);
                    xml.WriteElementString("video", "thumbnail_loc", Videos, video.ThumbnailUrl);
                    xml.WriteElementString("video", "title", Videos, video.Title);
                    xml.WriteElementString("video", "description", Videos, video.Description);
                    xml.WriteElementString("video", "content_loc", Videos, video.ContentUrl);
                    xml.WriteEndElement();
                }
                if (entry.News is { } news)
                {
                    xml.WriteStartElement("news", "news", News);
                    xml.WriteStartElement("news", "publication", News);
                    xml.WriteElementString("news", "name", News, "Fuse Hostels & Travel");
                    xml.WriteElementString("news", "language", News, "en");
                    xml.WriteEndElement();
                    xml.WriteElementString("news", "publication_date", News, Date(news.Published));
                    xml.WriteElementString("news", "title", News, news.Title);
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        return text.ToString();
    }

    private static string Date(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>So the XML declaration says utf-8 (a StringWriter would say utf-16).</summary>
    private sealed class StringWriterUtf8 : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
