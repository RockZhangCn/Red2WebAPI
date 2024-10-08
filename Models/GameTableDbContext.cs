using Microsoft.EntityFrameworkCore;

namespace Red2WebAPI.Models
{
    public class GameTableDbContext : DbContext
    {
        public GameTableDbContext(DbContextOptions<GameTableDbContext> options)
            : base(options) { 
                
            }

        public DbSet<GameTable> Tables => Set<GameTable>();
        public DbSet<Player> Players=> Set<Player>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<GameTable>()
                .HasMany(t => t.Players)
                .WithOne(p => p.GameTable);

            modelBuilder.Entity<Player>()
                .HasOne(p => p.GameTable)
                .WithMany(t => t.Players) 
                .HasForeignKey(p => p.TableId);
        }
        
    }
}