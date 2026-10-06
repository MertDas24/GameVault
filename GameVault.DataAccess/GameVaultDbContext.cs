using GameVault.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GameVault.DataAccess;

public class GameVaultDbContext(DbContextOptions<GameVaultDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int NotesMaxLength = 4000;
    public const int CoverUrlMaxLength = 500;
    public const int DisplayNameMaxLength = 50;
    public const int ReferenceNameMaxLength = 50;
    public const int CollectionNameMaxLength = 40;

    public DbSet<LibraryGame> LibraryGames => Set<LibraryGame>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<GameCollection> Collections => Set<GameCollection>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<RateLimitCounter> RateLimitCounters => Set<RateLimitCounter>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Tüm zamanlar UTC saklanır; okurken DateTimeKind.Utc olarak işaretlenir.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity'nin kendi tablo konfigürasyonu önce çalışmalı

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.DisplayName).HasMaxLength(DisplayNameMaxLength);
        });

        builder.Entity<Genre>(entity =>
        {
            entity.Property(g => g.Name).IsRequired().HasMaxLength(ReferenceNameMaxLength);
            entity.HasIndex(g => g.Name).IsUnique();
        });

        builder.Entity<Platform>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(ReferenceNameMaxLength);
            entity.HasIndex(p => p.Name).IsUnique();
        });

        builder.Entity<LibraryGame>(entity =>
        {
            entity.ToTable("LibraryGames", t =>
            {
                t.HasCheckConstraint("CK_LibraryGames_Rating", "[Rating] IS NULL OR ([Rating] >= 1 AND [Rating] <= 10)");
                t.HasCheckConstraint("CK_LibraryGames_PlaytimeHours", "[PlaytimeHours] >= 0 AND [PlaytimeHours] <= 100000");
                t.HasCheckConstraint("CK_LibraryGames_Status", "[Status] >= 0 AND [Status] <= 5");
                t.HasCheckConstraint("CK_LibraryGames_RawgId", "[RawgId] IS NULL OR [RawgId] > 0");
                t.HasCheckConstraint("CK_LibraryGames_EstimatedHours", "[EstimatedHours] IS NULL OR ([EstimatedHours] >= 1 AND [EstimatedHours] <= 1000)");
            });

            entity.Property(g => g.Title).IsRequired().HasMaxLength(TitleMaxLength);
            entity.Property(g => g.Description).HasMaxLength(DescriptionMaxLength);
            entity.Property(g => g.CoverImageUrl).HasMaxLength(CoverUrlMaxLength);
            entity.Property(g => g.Notes).HasMaxLength(NotesMaxLength);

            entity.HasOne(g => g.User)
                .WithMany(u => u.Games)
                .HasForeignKey(g => g.UserId)
                .OnDelete(DeleteBehavior.Cascade); // hesap silinince oyunları da silinir

            entity.HasOne(g => g.Platform)
                .WithMany(p => p.Games)
                .HasForeignKey(g => g.PlatformId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(g => g.Genres)
                .WithMany(g => g.Games)
                .UsingEntity<Dictionary<string, object>>(
                    "LibraryGameGenres",
                    r => r.HasOne<Genre>().WithMany().HasForeignKey("GenreId").OnDelete(DeleteBehavior.Restrict),
                    l => l.HasOne<LibraryGame>().WithMany().HasForeignKey("LibraryGameId").OnDelete(DeleteBehavior.Cascade),
                    j => j.HasKey("LibraryGameId", "GenreId"));

            // Koleksiyon silinince yalnızca bağlantılar silinir, oyunlar kalır.
            entity.HasMany(g => g.Collections)
                .WithMany(c => c.Games)
                .UsingEntity<Dictionary<string, object>>(
                    "LibraryGameCollections",
                    r => r.HasOne<GameCollection>().WithMany().HasForeignKey("CollectionId").OnDelete(DeleteBehavior.Cascade),
                    l => l.HasOne<LibraryGame>().WithMany().HasForeignKey("LibraryGameId").OnDelete(DeleteBehavior.Cascade),
                    j => j.HasKey("LibraryGameId", "CollectionId"));

            // Liste, filtre ve sıralama sorguları her zaman kullanıcıya göre başlar.
            entity.HasIndex(g => new { g.UserId, g.DateAdded });
            entity.HasIndex(g => new { g.UserId, g.Title });
            entity.HasIndex(g => new { g.UserId, g.Status });
            entity.HasIndex(g => new { g.UserId, g.IsFavorite });
            // Aynı RAWG oyunu aynı kullanıcının kütüphanesinde bir kez bulunabilir; farklı kullanıcılar ekleyebilir.
            entity.HasIndex(g => new { g.UserId, g.RawgId }).IsUnique().HasFilter("[RawgId] IS NOT NULL");
            entity.HasIndex(g => g.PlatformId);
        });

        builder.Entity<GameCollection>(entity =>
        {
            entity.Property(c => c.Name).IsRequired().HasMaxLength(CollectionNameMaxLength);
            // Aynı kullanıcıda aynı adlı iki koleksiyon olamaz (harmanlama gereği büyük/küçük harf duyarsız).
            entity.HasIndex(c => new { c.UserId, c.Name }).IsUnique();

            // Kullanıcı → koleksiyon zinciri cascade değil: kullanıcı → oyun → bağlantı yoluyla birlikte SQL Server'da
            // "birden çok cascade yolu" hatası verirdi. Hesap silinirken koleksiyonlar açıkça silinir (AccountService).
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserSession>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Ticket).IsRequired();
            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.ExpiresAtUtc);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OutboxEmail>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Kind).IsRequired().HasMaxLength(32).IsUnicode(false);
            entity.Property(o => o.Recipient).HasMaxLength(256);
            entity.Property(o => o.Subject).HasMaxLength(200);
            entity.Property(o => o.DedupKey).HasMaxLength(128).IsUnicode(false);
            entity.Property(o => o.LastError).HasMaxLength(100).IsUnicode(false);
            entity.HasIndex(o => new { o.Status, o.NextAttemptAtUtc });
            entity.HasIndex(o => o.CompletedAtUtc);
            entity.HasIndex(o => o.DedupKey).IsUnique().HasFilter("[DedupKey] IS NOT NULL");
        });

        builder.Entity<RateLimitCounter>(entity =>
        {
            entity.HasKey(c => c.Key);
            entity.Property(c => c.Key).HasMaxLength(128).IsUnicode(false);
            entity.HasIndex(c => c.WindowStartUtc);
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
