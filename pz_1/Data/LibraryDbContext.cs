using LibraryApp.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Data;

public class LibraryDbContext : DbContext
{
    public DbSet<Book> Books { get; set; }
    public DbSet<Author> Authors { get; set; }
    public DbSet<Genre> Genres { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "library.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Author configuration
        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.FirstName)
                  .IsRequired()
                  .HasMaxLength(100);
            entity.Property(a => a.LastName)
                  .IsRequired()
                  .HasMaxLength(100);
            entity.Property(a => a.Country)
                  .HasMaxLength(100);
        });

        // Genre configuration
        modelBuilder.Entity<Genre>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name)
                  .IsRequired()
                  .HasMaxLength(100);
            entity.Property(g => g.Description)
                  .HasMaxLength(500);
        });

        // Book configuration
        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Title)
                  .IsRequired()
                  .HasMaxLength(300);
            entity.Property(b => b.ISBN)
                  .IsRequired()
                  .HasMaxLength(20);
            entity.Property(b => b.PublishYear)
                  .IsRequired();
            entity.Property(b => b.QuantityInStock)
                  .IsRequired();

            // One Author -> Many Books (cascade delete)
            entity.HasOne(b => b.Author)
                  .WithMany(a => a.Books)
                  .HasForeignKey(b => b.AuthorId)
                  .OnDelete(DeleteBehavior.Cascade);

            // One Genre -> Many Books (cascade delete)
            entity.HasOne(b => b.Genre)
                  .WithMany(g => g.Books)
                  .HasForeignKey(b => b.GenreId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
