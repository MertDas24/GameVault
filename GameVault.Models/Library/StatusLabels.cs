using GameVault.Entities;

namespace GameVault.Models.Library;

public static class StatusLabels
{
    /// <summary>Arayüzde gösterilen sıra.</summary>
    public static readonly IReadOnlyList<GameStatus> Ordered =
    [
        GameStatus.NotStarted,
        GameStatus.Playing,
        GameStatus.Completed,
        GameStatus.OnHold,
        GameStatus.Dropped,
        GameStatus.Wishlist
    ];

    public static string Label(GameStatus status) => status switch
    {
        GameStatus.NotStarted => "Başlamadım",
        GameStatus.Playing => "Oynuyorum",
        GameStatus.Completed => "Bitirdim",
        GameStatus.OnHold => "Ara verdim",
        GameStatus.Dropped => "Bıraktım",
        GameStatus.Wishlist => "İstek listesi",
        _ => "Bilinmiyor"
    };

    /// <summary>CSS sınıfı ve URL parametresi için sabit anahtar.</summary>
    public static string Key(GameStatus status) => status switch
    {
        GameStatus.NotStarted => "not-started",
        GameStatus.Playing => "playing",
        GameStatus.Completed => "completed",
        GameStatus.OnHold => "on-hold",
        GameStatus.Dropped => "dropped",
        GameStatus.Wishlist => "wishlist",
        _ => "unknown"
    };

    public static GameStatus? FromKey(string? key)
    {
        foreach (var status in Ordered)
        {
            if (string.Equals(Key(status), key, StringComparison.Ordinal))
            {
                return status;
            }
        }

        return null;
    }
}
