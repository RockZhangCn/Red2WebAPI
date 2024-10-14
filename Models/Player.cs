using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization; // {{ edit_1 }}

namespace Red2WebAPI.Models
{
    public class Player
    {
        // 主键
        public int Id { get; set; }  // 主键的标准做法是使用一个唯一的 Id
        
        [Required] // Add this line
        public required int UserId { get; set; }
        
        [Required] // Add this line
        public required int AvatarId { get; set; }

        [Required] // Add this line
        public required string Nickname { get; set; }

        [Required] // Add this line
        public required int TableId { get; set; }

        [Required]
        public required int Pos { get; set; }

        public required string Message { get; set;}

        public required PlayerStatus Status { get; set; }

        [JsonIgnore]
        [Required]
        public GameTable? GameTable{ get; set; }

        public List<int>? Cards { get; set; } // Initialize with an empty list
    }


    public enum PlayerStatus {
        SEATED = 1,
        READY = 2,
        NOGRAB = 3,
        INPROGRESS = 4,
        WATCHING = 5, // cards count is zero.
    }
}