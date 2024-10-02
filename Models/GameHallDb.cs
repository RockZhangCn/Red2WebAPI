using Microsoft.EntityFrameworkCore;

namespace Red2WebAPI.Models
{
    class GameHallDb : DbContext
    {
        public GameHallDb(DbContextOptions<GameHallDb> options)
            : base(options) { 
                
            }

        public DbSet<Table> Tables => Set<Table>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Predefine 8 tables
            modelBuilder.Entity<Table>().HasData(
                new Table { Id = 1, }, // Replace with actual properties
                new Table { Id = 2, },
                new Table { Id = 3, },
                new Table { Id = 4, },
                new Table { Id = 5, },
                new Table { Id = 6, },
                new Table { Id = 7, },
                new Table { Id = 8, }
            );
        }
    }
}