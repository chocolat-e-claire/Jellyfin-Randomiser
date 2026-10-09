using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Randomizer.Services;

public interface IRandomSource
{
    int Next(int maxExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}

public sealed class RandomHistoryService
{
    readonly object gate = new();
    readonly Dictionary<Guid, LinkedList<Guid>> data = new();

    public IReadOnlyCollection<Guid> Get(Guid u, int max)
    {
        lock (gate)
        {
            return data.TryGetValue(u, out var h)
                ? h.Take(max).ToArray()
                : Array.Empty<Guid>();
        }
    }

    public void Add(Guid u, Guid id, int max)
    {
        if (max <= 0)
        {
            return;
        }

        lock (gate)
        {
            if (!data.TryGetValue(u, out var h))
            {
                data[u] = h = new();
            }

            var n = h.Find(id);
            if (n != null)
            {
                h.Remove(n);
            }

            h.AddFirst(id);
            while (h.Count > max)
            {
                h.RemoveLast();
            }
        }
    }
}

public sealed class RandomizerService
{
    readonly ILibraryManager lib;
    readonly IRandomSource rnd;
    readonly RandomHistoryService hist;

    public RandomizerService(ILibraryManager l, IRandomSource r, RandomHistoryService h)
    {
        lib = l;
        rnd = r;
        hist = h;
    }

    public IReadOnlyList<LibraryDto> Libraries(User u)
    {
        return lib.GetUserRootFolder()
            .Children
            .OfType<CollectionFolder>()
            .Where(x => x.IsVisibleStandalone(u))
            .Select(x => new LibraryDto(x.Id, x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<GenreDto> Genres(User u, Guid? libraryId, string type)
    {
        var kind = type.Equals("Movie", StringComparison.OrdinalIgnoreCase)
            ? BaseItemKind.Movie
            : BaseItemKind.Series;

        var query = Query(u, kind, libraryId, WatchedFilter.All);
        query.EnableTotalRecordCount = false;
        query.Limit = null;

        return lib.GetItemList(query)
            .SelectMany(x => x.Genres ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => new GenreDto(x))
            .ToArray();
    }

    public IReadOnlyList<LibraryDto> Search(
        User u,
        Guid? lid,
        string type,
        string? term,
        string? genre,
        int? limit)
    {
        var kind = type.Equals("Movie", StringComparison.OrdinalIgnoreCase)
            ? BaseItemKind.Movie
            : BaseItemKind.Series;

        var query = Query(u, kind, lid, WatchedFilter.All);
        query.SearchTerm = string.IsNullOrWhiteSpace(term) ? null : term.Trim();

        if (!string.IsNullOrWhiteSpace(genre))
        {
            query.Genres = new[] { genre.Trim() };
        }

        query.EnableTotalRecordCount = false;
        query.OrderBy = new[] { (ItemSortBy.SortName, SortOrder.Ascending) };

        if (limit.HasValue)
        {
            query.Limit = Math.Max(1, limit.Value);
        }
        else
        {
            query.Limit = null;
        }

        return lib.GetItemList(query)
            .Select(x => new LibraryDto(x.Id, x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RandomizeResult? Randomize(User u, RandomizeRequest r)
    {
        if (r.LibraryId.HasValue && lib.GetItemById<CollectionFolder>(r.LibraryId.Value, u) == null)
        {
            return null;
        }

        var recent = hist.Get(u.Id, Math.Clamp(r.AvoidRecent, 0, 20));

        BaseItem? x = r.Mode switch
        {
            RandomizationMode.RandomMovie => Pick(u, BaseItemKind.Movie, r.LibraryId, r.ItemIds, r.Watched, recent, false),
            RandomizationMode.RandomShow => Pick(u, BaseItemKind.Series, r.LibraryId, r.ItemIds, r.Watched, recent, false),
            RandomizationMode.RandomEpisode => PickEpisode(u, r, recent),
            _ => null
        };

        if (x == null)
        {
            return null;
        }

        hist.Add(u.Id, x.Id, Math.Clamp(r.AvoidRecent, 0, 20));
        var e = x as Episode;

        return new RandomizeResult
        {
            ItemId = x.Id,
            ItemType = x.GetType().Name,
            Name = x.Name,
            SeriesName = e?.SeriesName,
            SeasonNumber = e?.ParentIndexNumber,
            EpisodeNumber = e?.IndexNumber,
            Overview = x.Overview,
            Year = x.ProductionYear
        };
    }

    BaseItem? PickEpisode(User u, RandomizeRequest r, IReadOnlyCollection<Guid> recent)
    {
        var shows = r.ItemIds.Count > 0
            ? r.ItemIds.ToArray()
            : lib.GetItemList(Query(u, BaseItemKind.Series, r.LibraryId, r.Watched))
                .Where(x => !recent.Contains(x.Id))
                .Select(x => x.Id)
                .ToArray();

        if (shows.Length == 0)
        {
            return null;
        }

        if (r.Strategy == RandomizationStrategy.EqualShow)
        {
            foreach (var id in shows.OrderBy(_ => rnd.Next(int.MaxValue)))
            {
                var e = Pick(u, BaseItemKind.Episode, null, new[] { id }, r.Watched, recent, true);
                if (e != null)
                {
                    return e;
                }
            }

            return null;
        }

        return Pick(u, BaseItemKind.Episode, null, shows, r.Watched, recent, true);
    }

    BaseItem? Pick(
        User u,
        BaseItemKind k,
        Guid? lid,
        IReadOnlyCollection<Guid> ids,
        WatchedFilter w,
        IReadOnlyCollection<Guid> recent,
        bool idsAreAncestors)
    {
        var query = Query(u, k, lid, w);

        if (ids.Count > 0)
        {
            var visibleIds = ids
                .Where(id => lib.GetItemById<BaseItem>(id, u) != null)
                .ToArray();

            if (visibleIds.Length == 0)
            {
                return null;
            }

            if (lid.HasValue && lib.GetItemById<CollectionFolder>(lid.Value, u) == null)
            {
                return null;
            }

            if (idsAreAncestors)
            {
                query.AncestorIds = visibleIds;
            }
            else
            {
                query.ItemIds = visibleIds;
            }
        }

        if (recent.Count > 0)
        {
            query.ExcludeItemIds = recent.ToArray();
        }

        query.EnableTotalRecordCount = true;
        query.StartIndex = null;
        query.Limit = null;

        var count = lib.GetItemsResult(query).TotalRecordCount;
        if (count <= 0)
        {
            return null;
        }

        query.StartIndex = rnd.Next(count);
        query.Limit = 1;
        query.EnableTotalRecordCount = false;

        return lib.GetItemList(query).FirstOrDefault();
    }

    static InternalItemsQuery Query(User u, BaseItemKind k, Guid? lid, WatchedFilter w)
    {
        var query = new InternalItemsQuery(u)
        {
            IncludeItemTypes = new[] { k },
            IsFolder = k == BaseItemKind.Series,
            Recursive = true,
            IsPlayed = w switch
            {
                WatchedFilter.Watched => true,
                WatchedFilter.Unwatched => false,
                _ => null
            }
        };

        if (lid.HasValue)
        {
            query.AncestorIds = new[] { lid.Value };
        }

        return query;
    }
}
