using System.ComponentModel.DataAnnotations; // {{ edit_1 }}

namespace Red2WebAPI.Models
{
    public class User
    {
        // 主键
        public int Id { get; set; }  // 主键的标准做法是使用一个唯一的 Id
        [Required] // Add this line
        public required string Email { get; set; }
        [Required] // Add this line
        public required string Nickname { get; set; }
        
        [Required]
        public required int Avatar { get; set; } // Made the property nullable

        [Required]
        public required string Password { get; set; } 

        public string Digest { get; set; } = null!;

        public string Salt { get; set; } = null!;

        public int Score { get; set; }

        public DateTime Createtime { get; set; }
    }

    // Define a DTO class
    public class LoginUserDto
    {
        public required bool Success { get; set; }
        public required string Message { get; set; }

        public  string? Email { get; set; }

        public  string? Nickname { get; set; }

        public  int? Avatar { get; set; }
        // Add other properties you want to expose, but exclude the password
    }

}