using Microsoft.EntityFrameworkCore;

namespace Red2WebAPI.Models
{
    class UserDb : DbContext
    {
        public UserDb(DbContextOptions<UserDb> options)
            : base(options) { 
                if (!Users.Any(u => u.Email == "rock.zhang.cn@gmail.com")) {
                    Users.Add(new Register {
                        Email = "rock.zhang.cn@gmail.com",
                        Id = 1,
                        Nickname = "rockzhang", // Example field
                        Password = "12345678", // Example field
                        Avatar = "/avatar/icon_1.png"
                        // Add any additional fields as necessary
                    });
                    SaveChanges();
                }
            }

        public DbSet<Register> Users => Set<Register>();

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