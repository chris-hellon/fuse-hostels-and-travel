namespace Fuse.Contracts.Common;

/// <summary>The search-engine fields every public page has (galleries have only these).</summary>
public interface IMetaFields
{
    string? MetaKeywords { get; set; }
    string? MetaDescription { get; set; }
}

/// <summary>A page with its own web address (travel guides add this to <see cref="IMetaFields"/>).</summary>
public interface ISlugField
{
    string? UrlSlug { get; set; }
}

/// <summary>The full SEO fields pages share (tours, properties...), edited by one admin component.</summary>
public interface ISeoFields : IMetaFields, ISlugField
{
    /// <summary>Replaces the page's &lt;title&gt;.</summary>
    string? SeoPageTitle { get; set; }
    /// <summary>The page heading. The public pages fall back to the name when it's empty.</summary>
    string? H1 { get; set; }
    /// <summary>The sub heading.</summary>
    string? H2 { get; set; }
    /// <summary>Extra markup for the page head, wrapped in &lt;script&gt; tags.</summary>
    string? CustomSeoScripts { get; set; }
}

/// <summary>An image in an ordered gallery: an existing one (<see cref="Id"/>) or a new upload (<see cref="Url"/>).</summary>
public sealed record GalleryRef(DefaultIdType? Id, string? Url);

public sealed record GalleryImageItem(DefaultIdType Id, string ImagePath, string ThumbnailImagePath);
