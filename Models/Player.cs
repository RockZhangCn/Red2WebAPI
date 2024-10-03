using System.ComponentModel.DataAnnotations; // {{ edit_1 }}

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

        [Required]
        public required GameTable GameTable{ get; set; }

        public List<int> Cards { get; set; } = new List<int>(); // Initialize with an empty list
    }


 

}