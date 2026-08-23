using VRChat.API.Model;

namespace VRCUFM.VRChat;

public partial class APIService
{
    /// <summary>Add a user to a friend favorite group (group_0 .. group_3).</summary>
    public async Task AddFriendFavoriteAsync(string userId, string groupTag)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId required");
        if (string.IsNullOrWhiteSpace(groupTag))
            groupTag = "group_0";

        var req = new AddFavoriteRequest(
            type: FavoriteType.Friend,
            favoriteId: userId,
            tags: new List<string> { groupTag }
        );

        try
        {
            await Favorites.AddFavoriteAsync(req);
        }
        catch (Exception ex)
        {
            // Already favorited is fine
            if (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
                return;
            throw;
        }
    }

    /// <summary>
    /// Remove a user from friend favorites.
    /// VRChat RemoveFavorite expects the *favorite row id*, not the user id.
    /// </summary>
    public async Task RemoveFriendFavoriteAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId required");

        string? recordId = null;
        for (int offset = 0; ; offset += 100)
        {
            var page = await Favorites.GetFavoritesAsync(type: "friend", n: 100, offset: offset);
            foreach (var f in page)
            {
                // FavoriteId = target user id; Id = favorite row id used by RemoveFavorite
                if (string.Equals(f.FavoriteId, userId, StringComparison.OrdinalIgnoreCase))
                {
                    recordId = f.Id;
                    break;
                }
            }
            if (recordId != null || page.Count < 100) break;
        }

        if (recordId == null)
            return; // not favorited

        await Favorites.RemoveFavoriteAsync(recordId);
    }


    /// <summary>Clear ALL friends from one favorite group (group_0 .. group_3).</summary>
    public async Task ClearFriendFavoriteGroupAsync(string groupTag)
    {
        if (string.IsNullOrWhiteSpace(groupTag))
            groupTag = "group_0";
        if (string.IsNullOrEmpty(CurrentUserId))
            throw new InvalidOperationException("Not logged in");

        await Favorites.ClearFavoriteGroupAsync(
            FavoriteType.Friend,
            groupTag,
            CurrentUserId);
    }

    /// <summary>Remove every friend favorite across all groups.</summary>
    public async Task ClearAllFriendFavoritesAsync()
    {
        for (int offset = 0; ; offset += 100)
        {
            var page = await Favorites.GetFavoritesAsync(type: "friend", n: 100, offset: 0);
            if (page.Count == 0) break;
            foreach (var f in page)
            {
                if (!string.IsNullOrEmpty(f.Id))
                    await Favorites.RemoveFavoriteAsync(f.Id);
            }
            // always offset 0 because list shrinks as we delete
            if (page.Count < 100) break;
        }
    }
}
