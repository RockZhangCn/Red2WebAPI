using System.ComponentModel.DataAnnotations; // {{ edit_1 }}

namespace Red2WebAPI.Models
{
    public class GameTable
    {
        public int Id { get; set; }

        public required int TableId { get; set; }

        public required GameStatus GameStatus { get; set;} = GameStatus.WAITING;

        public int? ActivePos { get; set;}

        public List<Player> Players{ get; set; } = new List<Player>();

        public List<int> CentreCards { get; set; } = new List<int>();

        public int? CentreShotPlayerPos { get; set;}

        public int NextScore { get; set;} = 4;

        public List<int> Red2TeamPos { get; set; } = new List<int>();
    }

    public enum GameStatus {
        WAITING = 1,
        GRAB2,
        YIELD2,
        INPROGRESS,
        END,
    }
}
