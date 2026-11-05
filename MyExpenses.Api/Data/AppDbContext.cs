using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Income> Incomes => Set<Income>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<SecurityChallenge> SecurityChallenges => Set<SecurityChallenge>();
    public DbSet<TelegramLink> TelegramLinks => Set<TelegramLink>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SecurityChallenge>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasMaxLength(Limits.Id);
            entity.Property(c => c.Purpose).HasMaxLength(16);
            entity.HasIndex(c => new { c.UserId, c.Purpose });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TelegramLink>(entity =>
        {
            entity.HasKey(l => l.UserId);
            entity.Property(l => l.TokenHash).HasMaxLength(64);
            entity.HasIndex(l => l.TokenHash).IsUnique();
            entity.HasOne<AppUser>().WithOne().HasForeignKey<TelegramLink>(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecoveryCode>(entity =>
        {
            entity.HasKey(r => new { r.UserId, r.CodeHash });
            entity.Property(r => r.CodeHash).HasMaxLength(64);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Category>(entity =>
        {
            entity.HasKey(c => new { c.UserId, c.Id });
            entity.Property(c => c.Id).HasMaxLength(Limits.Id);
            entity.Property(c => c.Name).HasMaxLength(Limits.Name);
            entity.Property(c => c.Icon).HasMaxLength(Limits.Icon);
            entity.Property(c => c.Color).HasMaxLength(Limits.Color);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Item>(entity =>
        {
            entity.HasKey(i => new { i.UserId, i.Id });
            entity.Property(i => i.Id).HasMaxLength(Limits.Id);
            entity.Property(i => i.CategoryId).HasMaxLength(Limits.Id);
            entity.Property(i => i.Name).HasMaxLength(Limits.Name);
            entity.HasOne<Category>().WithMany()
                .HasForeignKey(i => new { i.UserId, i.CategoryId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Income>(entity =>
        {
            entity.HasKey(i => new { i.UserId, i.Id });
            entity.Property(i => i.Id).HasMaxLength(Limits.Id);
            entity.Property(i => i.Source).HasMaxLength(Limits.Name);
            entity.Property(i => i.Note).HasMaxLength(Limits.Note);
            entity.HasIndex(i => new { i.UserId, i.Date });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(t => t.HasCheckConstraint("CK_Incomes_Amount", "\"Amount\" > 0"));
        });

        builder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.Id });
            entity.Property(e => e.Id).HasMaxLength(Limits.Id);
            entity.Property(e => e.CategoryId).HasMaxLength(Limits.Id);
            entity.Property(e => e.ItemId).HasMaxLength(Limits.Id);
            entity.Property(e => e.Quantity).HasMaxLength(Limits.Name);
            entity.Property(e => e.Note).HasMaxLength(Limits.Note);
            entity.HasIndex(e => new { e.UserId, e.Date });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

            // A category or item can't be deleted while expenses use it. NoAction is checked at
            // the end of the statement, so deleting a whole account can still remove all of them.
            entity.HasOne<Category>().WithMany()
                .HasForeignKey(e => new { e.UserId, e.CategoryId })
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Item>().WithMany()
                .HasForeignKey(e => new { e.UserId, e.ItemId })
                .OnDelete(DeleteBehavior.NoAction);
            entity.ToTable(t => t.HasCheckConstraint("CK_Expenses_Amount", "\"Amount\" > 0"));
        });

        builder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(s => s.UserId);
            entity.Property(s => s.CurrencyCode).HasMaxLength(Limits.CurrencyCode);
            entity.Property(s => s.CurrencySymbol).HasMaxLength(Limits.CurrencySymbol);
            entity.Property(s => s.PeriodType).HasMaxLength(5);
            entity.Property(s => s.Theme).HasMaxLength(6);
            entity.HasOne<AppUser>().WithOne().HasForeignKey<UserSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(t => t.HasCheckConstraint("CK_UserSettings_MonthStartDay", "\"MonthStartDay\" BETWEEN 1 AND 28"));
        });
    }
}
