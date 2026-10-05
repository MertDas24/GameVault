namespace GameVault.Entities;

/// <summary>
/// Veritabanında tutulan sabit pencereli oran sınırı sayacı. Tüm uygulama
/// örnekleri aynı tabloyu paylaştığı için sınırlar çoklu instance'ta tutarlıdır.
/// </summary>
public class RateLimitCounter
{
    public string Key { get; set; } = string.Empty;
    public DateTime WindowStartUtc { get; set; }
    public int Count { get; set; }
}
