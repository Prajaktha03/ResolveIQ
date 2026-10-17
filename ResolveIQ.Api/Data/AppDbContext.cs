using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<User> Users => Set<User>();

    public DbSet<TicketComment> TicketComments => Set<TicketComment>();

    public DbSet<TicketHistory> TicketHistories => Set<TicketHistory>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.Email)
            .HasMaxLength(256);

        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasMaxLength(20);

        // Ticket creator
        modelBuilder.Entity<Ticket>()
            .HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Assigned agent
        modelBuilder.Entity<Ticket>()
            .HasOne(t => t.AssignedToUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ticket search and filter indexes
        modelBuilder.Entity<Ticket>()
            .HasIndex(t => t.Status);

        modelBuilder.Entity<Ticket>()
            .HasIndex(t => t.Priority);

        modelBuilder.Entity<Ticket>()
            .HasIndex(t => t.CreatedAt);

        // Ticket comments
        modelBuilder.Entity<TicketComment>()
            .HasOne(c => c.Ticket)
            .WithMany()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TicketComment>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TicketComment>()
            .Property(c => c.Content)
            .HasMaxLength(2000);

        // Ticket history
        modelBuilder.Entity<TicketHistory>()
            .HasOne(h => h.Ticket)
            .WithMany()
            .HasForeignKey(h => h.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TicketHistory>()
            .HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TicketHistory>()
            .Property(h => h.Action)
            .HasMaxLength(50);

        modelBuilder.Entity<TicketHistory>()
            .Property(h => h.OldValue)
            .HasMaxLength(500);

        modelBuilder.Entity<TicketHistory>()
            .Property(h => h.NewValue)
            .HasMaxLength(500);

        modelBuilder.Entity<TicketHistory>()
            .HasIndex(h => new { h.TicketId, h.CreatedAt });

        // Password reset tokens
        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PasswordResetToken>()
            .HasIndex(t => t.TokenHash);

        modelBuilder.Entity<PasswordResetToken>()
            .Property(t => t.TokenHash)
            .HasMaxLength(128);

        // Notifications
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.Ticket)
            .WithMany()
            .HasForeignKey(n => n.TicketId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .Property(n => n.Type)
            .HasMaxLength(50);

        modelBuilder.Entity<Notification>()
            .Property(n => n.Title)
            .HasMaxLength(200);

        modelBuilder.Entity<Notification>()
            .Property(n => n.Message)
            .HasMaxLength(1000);

        modelBuilder.Entity<Notification>()
            .Property(n => n.DeduplicationKey)
            .HasMaxLength(256);

        modelBuilder.Entity<Notification>()
            .HasIndex(n => n.UserId);

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead });

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.CreatedAt });

        modelBuilder.Entity<Notification>()
            .HasIndex(n => n.DeduplicationKey)
            .IsUnique();

        // Knowledge articles
        modelBuilder.Entity<KnowledgeArticle>()
            .HasOne(k => k.AuthorUser)
            .WithMany()
            .HasForeignKey(k => k.AuthorUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<KnowledgeArticle>()
            .Property(k => k.Title)
            .HasMaxLength(200);

        modelBuilder.Entity<KnowledgeArticle>()
            .Property(k => k.Category)
            .HasMaxLength(50);

        modelBuilder.Entity<KnowledgeArticle>()
            .Property(k => k.Tags)
            .HasMaxLength(200);

        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(k => k.Category);

        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(k => k.IsPublished);

        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(k => k.CreatedAt);
    }
}