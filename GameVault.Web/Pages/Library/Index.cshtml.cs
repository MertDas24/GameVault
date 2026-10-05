using GameVault.Business.Library;
using GameVault.Models.Library;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

public sealed class IndexModel(LibraryService library, ReferenceDataService referenceData) : AppPageModel
{
    public LibraryQuery Query { get; private set; } = new();
    public PagedResult<GameCard> Result { get; private set; } = new([], 1, LibraryQuery.DefaultPageSize, 0);
    public int LibraryTotal { get; private set; }
    public IReadOnlyList<ReferenceItem> Platforms { get; private set; } = [];
    public IReadOnlyList<ReferenceItem> Genres { get; private set; } = [];
    public string CurrentUrl { get; private set; } = "/Library";

    public async Task OnGetAsync(
        [FromQuery] string? q, [FromQuery] string? status, [FromQuery] string? platform, [FromQuery] string? genre,
        [FromQuery] string? fav, [FromQuery] string? sort, [FromQuery] string? page, [FromQuery] string? size,
        CancellationToken ct)
    {
        Query = LibraryQuery.FromRaw(q, status, platform, genre, fav, sort, page, size);
        var userId = CurrentUserId;
        Result = await library.SearchAsync(userId, Query, ct);
        LibraryTotal = Query.HasFilters ? await library.CountAsync(userId, ct) : Result.TotalCount;
        Platforms = await referenceData.GetPlatformsAsync(ct);
        Genres = await referenceData.GetGenresAsync(ct);
        CurrentUrl = PageUrl(Result.Page);
    }

    /// <summary>Geçerli filtreleri koruyarak belirtilen sayfanın adresini üretir.</summary>
    public string PageUrl(int page)
    {
        var qb = new QueryBuilder();
        if (Query.Search is { } s)
        {
            qb.Add("q", s);
        }

        if (Query.Status is { } st)
        {
            qb.Add("status", StatusLabels.Key(st));
        }

        if (Query.PlatformId is { } p)
        {
            qb.Add("platform", p.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (Query.GenreId is { } g)
        {
            qb.Add("genre", g.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (Query.FavoritesOnly)
        {
            qb.Add("fav", "1");
        }

        if (Query.Sort != LibrarySort.AddedDesc)
        {
            qb.Add("sort", Query.SortKey);
        }

        if (Query.PageSize != LibraryQuery.DefaultPageSize)
        {
            qb.Add("size", Query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (page > 1)
        {
            qb.Add("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return "/Library" + qb.ToQueryString();
    }
}
