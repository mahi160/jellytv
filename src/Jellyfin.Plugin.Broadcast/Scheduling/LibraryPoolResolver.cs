using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Executes a Programming Block's filters against the Jellyfin library and returns the matching pool.
/// Per ADR 0001, the pool is the intersection of what every Jellyfin user can see — no per-user personalization.
/// </summary>
public class LibraryPoolResolver : IMediaPoolResolver
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryPoolResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="userManager">Jellyfin's user manager.</param>
    public LibraryPoolResolver(ILibraryManager libraryManager, IUserManager userManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduleCandidate> GetMatchingPool(ProgrammingBlock block) =>
        BaseItemCandidateAdapter.ToCandidates(GetMatchingItems(block));

    /// <summary>
    /// Gets the raw library items matching a Programming Block's filters, visible to every user.
    /// </summary>
    /// <param name="block">The Programming Block.</param>
    /// <returns>The matching items.</returns>
    public IReadOnlyList<BaseItem> GetMatchingItems(ProgrammingBlock block)
    {
        var query = RulesEngine.BuildQuery(block);

        if (block.Libraries.Count > 0)
        {
            query.TopParentIds = ResolveLibraryIds(block.Libraries);
        }

        // A disabled user is never actually watching — including them in the intersection would let one
        // forgotten disabled account starve the whole channel down to an empty pool.
        var users = _userManager.Users.Where(u => !IsDisabled(u)).ToList();
        if (users.Count == 0)
        {
            return _libraryManager.GetItemList(query);
        }

        HashSet<Guid>? intersectedIds = null;
        var itemsById = new Dictionary<Guid, BaseItem>();

        foreach (var user in users)
        {
            query.User = user;
            var items = _libraryManager.GetItemList(query);
            var idsForUser = new HashSet<Guid>();
            foreach (var item in items)
            {
                idsForUser.Add(item.Id);
                itemsById[item.Id] = item;
            }

            intersectedIds = intersectedIds is null ? idsForUser : Intersect(intersectedIds, idsForUser);
        }

        if (intersectedIds is null || intersectedIds.Count == 0)
        {
            return Array.Empty<BaseItem>();
        }

        return intersectedIds.Select(id => itemsById[id]).ToList();
    }

    private static bool IsDisabled(Jellyfin.Database.Implementations.Entities.User user) =>
        user.Permissions.Any(p => p.Kind == PermissionKind.IsDisabled && p.Value);

    private static HashSet<Guid> Intersect(HashSet<Guid> a, HashSet<Guid> b)
    {
        a.IntersectWith(b);
        return a;
    }

    private Guid[] ResolveLibraryIds(IReadOnlyList<string> names)
    {
        var root = _libraryManager.GetUserRootFolder();
        return root.Children
            .Where(child => names.Contains(child.Name, StringComparer.OrdinalIgnoreCase))
            .Select(child => child.Id)
            .ToArray();
    }
}
