using Microsoft.EntityFrameworkCore;

namespace Red2WebAPI.Models
{
    class UserDb : DbContext
    {
        public UserDb(DbContextOptions<UserDb> options)
            : base(options) { }

        public DbSet<Register> Users => Set<Register>();
    }

}