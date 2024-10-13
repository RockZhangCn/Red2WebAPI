using System.ComponentModel.DataAnnotations; // {{ edit_1 }}

namespace Red2WebAPI.Models
{
    public class GameTable
    {
        public int Id { get; set; }

        public required int TableId { get; set; }

        public required GameStatus GameStatus { get; set;}

        public int? ActivePos { get; set;}

        public List<Player> Players{ get; set; } = new List<Player>();

        public List<int> CentreCards { get; set; } = new List<int>();
    }

    public enum GameStatus {
        WAITING = 1,
        GRAB2,
        YIELD2,
        INPROGRESS,
    }
}
