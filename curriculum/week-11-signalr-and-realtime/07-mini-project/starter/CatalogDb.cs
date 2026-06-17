// Crunch.Chat / src/Server / CatalogDb.cs
//
// EF Core DbContext for the durable message store. One table: Messages.
// Used by MessageStore.cs to persist every accepted broadcast so that
// (a) FetchSince can fill the reconnect gap, and (b) StreamHistory can
// progressively replay long histories.
//
// Citations:
//   EF Core DbContext: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
//   Npgsql provider:   https://www.npgsql.org/efcore/
//   Indexes for query: https://learn.microsoft.com/en-us/ef/core/modeling/indexes

#nullable enable
using Microsoft.EntityFrameworkCore;

namespace Crunch.Chat;

public sealed class CatalogDb : DbContext
{
    public CatalogDb(DbContextOptions<CatalogDb> options) : base(options) { }

    public DbSet<MessageRow> Messages => Set<MessageRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var msg = builder.Entity<MessageRow>();
        msg.Property(m => m.Id).ValueGeneratedOnAdd();
        msg.Property(m => m.Room).HasMaxLength(64).IsRequired();
        msg.Property(m => m.User).HasMaxLength(128).IsRequired();
        msg.Property(m => m.Text).HasMaxLength(4096).IsRequired();
        msg.Property(m => m.ClientMessageId).HasMaxLength(64).IsRequired();
        msg.Property(m => m.Timestamp).IsRequired();

        // Index for FetchSince: WHERE Room = @r AND Id > @sinceId ORDER BY Id.
        // The (Room, Id) compound index is the right shape for that query.
        msg.HasIndex(m => new { m.Room, m.Id })
           .HasDatabaseName("ix_messages_room_id");

        // Unique on (User, ClientMessageId) for an extra layer of dedupe
        // against race conditions in the in-memory DedupeCache.
        msg.HasIndex(m => new { m.User, m.ClientMessageId })
           .IsUnique()
           .HasDatabaseName("ux_messages_user_client_id");
    }
}

public sealed class MessageRow
{
    public long Id { get; set; }
    public string Room { get; set; } = "";
    public string User { get; set; } = "";
    public string Text { get; set; } = "";
    public string ClientMessageId { get; set; } = "";
    public long Timestamp { get; set; }
}

// The shape we broadcast and stream. Distinct from MessageRow so we can
// shape the wire payload without leaking EF columns.
public sealed record LogEntry(
    long Id,
    string Room,
    string User,
    string Text,
    long Timestamp);
