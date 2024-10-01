using Microsoft.EntityFrameworkCore;

namespace Red2WebAPI.Models
{
    class UserDb : DbContext
    {
        public UserDb(DbContextOptions<UserDb> options)
            : base(options) { 
                Users.Add(new Register {
                    Email = "rock.zhang.cn@gmail.com",
                    Id = 1,
                    Nickname = "rockzhang", // Example field
                    Password = "12345678", // Example field
                    Avatar = "/avatar/icon_1.png"
                    // Add any additional fields as necessary
                });
            }

        public DbSet<Register> Users => Set<Register>();
    }

}