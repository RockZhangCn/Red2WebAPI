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
    }

}