using System.ComponentModel.DataAnnotations; // {{ edit_1 }}

namespace Red2WebAPI.Models
{
    public class Register
    {
        // 主键
        public int Id { get; set; }  // 主键的标准做法是使用一个唯一的 Id
        [Required] // Add this line
        public required string Email { get; set; }
        [Required] // Add this line
        public required string Nickname { get; set; }
        [Required] // Add this line
        public required string Password { get; set; }
        [Required]
        public required string Avatar { get; set; } // Made the property nullable
    }


        // Define a DTO class
    public class LoginUserDto
    {
        public required bool Success { get; set; }
        public required string Message { get; set; }

        public  string? Email { get; set; }

        public  string? Nickname { get; set; }

        public  string? Avatar { get; set; }
        // Add other properties you want to expose, but exclude the password
    }

}