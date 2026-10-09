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
    private readonly object gate = new();
    private readonly Dictionary<Guid, LinkedList<Guid>> data = new();

    public IReadOnlyCollection<Guid> Get(Guid userId, int max)
    {
        lock (gate)
        {
            return data.TryGetValue(userId, out var history)
                ? history.Take(max).ToArray()
                : Array.Empty<Guid>();
        }
    }

    public void Add(Guid userId, Guid id, int max)
    {
        if (max <= 0)
        {
            return;
        }

        lock (gate)
        {
            if (!data.TryGetValue(userId, out var history))
            {
                data[userId] = history = new LinkedList<Guid>();
            }

            var node = history.Find(id);
            if (node is not null)
            {
                history.Remove(node);
            }

            history.AddFirst(id);
            while (history.Count > max)
            {
                history.RemoveLast();
            }
        }
    }
}

public sealed class RandomizerService
{
    private readonly ILibraryManager lib;
    private readonly IRandomSource rnd;
    private readonly RandomHistoryService hist;

    public RandomizerService(ILibraryManager libraryManager, IRandomSource randomSource, RandomHistoryService history)
    {
        lib = libraryManager;
        rnd = randomSource;
        hist = history;
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

    public IReadOnlyList<string> Genres(User u, Guid? lid, string type)
    {
        var kind = type.Equals("Movie", StringComparison.OrdinalIgnoreCase)
            ? BaseItemKind.Movie
            : BaseItemKind.Series;

        if (lid.HasValue && lib.GetItemById<CollectionFolder>(lid.Value, u) is null)
        {
            return Array.Empty<string>();
        }

        var query = Query(u, kind, lid, WatchedFilter.All);
        query.Limit = null;
        query.EnableTotalRecordCount = false;

        return lib.GetGenres(query)
            .Items
            .Select(x => x.Item.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<LibraryDto> Search(
        User u,
        Guid? lid,
        string type,
        string? genre,
        string? term,
        int limit)
    {
        var kind = type.Equals("Movie", StringComparison.OrdinalIgnoreCase)
            ? BaseItemKind.Movie
            : BaseItemKind.Series;

        if (lid.HasValue && lib.GetItemById<CollectionFolder>(lid.Value, u) is null)
        {
            return Array.Empty<LibraryDto>();
        }

        var query = Query(u, kind, lid, WatchedFilter.All);
        query.SearchTerm = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
        if (!string.IsNullOrWhiteSpace(genre))
        {
            query.Genres = new[] { genre.Trim() };
        }

        query.Limit = limit > 0 ? Math.Clamp(limit, 1, 5000) : null;
        query.EnableTotalRecordCount = false;

        return lib.GetItemList(query)
            .Select(x => new LibraryDto(x.Id, x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RandomizeResult? Randomize(User u, RandomizeRequest r)
    {
        if (r.LibraryId.HasValue && lib.GetItemById<CollectionFolder>(r.LibraryId.Value, u) is null)
        {
            return null;
        }

        var genre = string.IsNullOrWhiteSpace(r.Genre) ? null : r.Genre.Trim();
        var recent = hist.Get(u.Id, Math.Clamp(r.AvoidRecent, 0, 20));

        BaseItem? x = r.Mode switch
        {
            RandomizationMode.RandomMovie => Pick(
                u,
                BaseItemKind.Movie,
                r.LibraryId,
                r.ItemIds,
                r.Watched,
                recent,
                false,
                genre),
            RandomizationMode.RandomShow => Pick(
                u,
                BaseItemKind.Series,
                r.LibraryId,
                r.ItemIds,
                r.Watched,
                recent,
                false,
                genre),
            RandomizationMode.RandomEpisode => PickEpisode(u, r, recent, genre),
            _ => null
        };

        if (x is null)
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

    private BaseItem? PickEpisode(
        User u,
        RandomizeRequest r,
        IReadOnlyCollection<Guid> recent,
        string? genre)
    {
        var shows = r.ItemIds.Count > 0
            ? r.ItemIds.ToArray()
            : lib.GetItemList(Query(u, BaseItemKind.Series, r.LibraryId, r.Watched, genre))
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
                var episode = Pick(
                    u,
                    BaseItemKind.Episode,
                    null,
                    new[] { id },
                    r.Watched,
                    recent,
                    true,
                    genre);

                if (episode is not null)
                {
                    return episode;
                }
            }

            return null;
        }

        return Pick(
            u,
            BaseItemKind.Episode,
            null,
            shows,
            r.Watched,
            recent,
            true,
            genre);
    }

    private BaseItem? Pick(
        User u,
        BaseItemKind kind,
        Guid? lid,
        IReadOnlyCollection<Guid> ids,
        WatchedFilter watched,
        IReadOnlyCollection<Guid> recent,
        bool idsAreAncestors,
        string? genre)
    {
        var query = Query(u, kind, lid, watched, genre);

        if (ids.Count > 0)
        {
            var visibleIds = ids
                .Where(id => lib.GetItemById<BaseItem>(id, u) is not null)
                .ToArray();

            if (visibleIds.Length == 0)
            {
                return null;
            }

            if (lid.HasValue && lib.GetItemById<CollectionFolder>(lid.Value, u) is null)
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

    private static InternalItemsQuery Query(
        User u,
        BaseItemKind kind,
        Guid? lid,
        WatchedFilter watched,
        string? genre = null)
    {
        var query = new InternalItemsQuery(u)
        {
            IncludeItemTypes = new[] { kind },
            IsFolder = kind == BaseItemKind.Series,
            Recursive = true,
            IsPlayed = watched switch
            {
                WatchedFilter.Watched => true,
                WatchedFilter.Unwatched => false,
                _ => null
            }
        };

        if (!string.IsNullOrWhiteSpace(genre))
        {
            query.Genres = new[] { genre.Trim() };
        }

        if (lid.HasValue)
        {
            query.AncestorIds = new[] { lid.Value };
        }

        return query;
    }
}
