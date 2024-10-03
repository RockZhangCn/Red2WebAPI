using System.ComponentModel.DataAnnotations; // {{ edit_1 }}

namespace Red2WebAPI.Models
{
    public class GameTable
    {
        public int Id { get; set; }

        public List<Player> Players{ get; set; } = new List<Player>();

        public int GameStatus { get; set;}
    }
}