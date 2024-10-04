using Red2WebAPI.Models;

namespace Red2WebAPI.Seed;

public static class DbInitializer
{
    public static void Initialize(GameTableDbContext context)
    {
         if (context.Tables.Any()) {
            return;
         }

         for (int i = 0; i < 8; i++) {
            var table = new GameTable {
                TableId = i+1,
                GameStatus = GameStatus.WAITING,
            };

            context.Tables.Add(table);
            context.SaveChanges();
         }

     }

}
