using System.ComponentModel.DataAnnotations; // {{ edit_1 }}

namespace Red2WebAPI.Models
{
    public class Table
    {
        public int Id { get; set; }

        public TableUser? LeftUser { get; set; }

        public TableUser? BottomUser { get; set; }

        public TableUser? RightUser { get; set; }

        public TableUser? TopUser { get; set; }
    }

    public class TableUser
    {
        public int Id { get; set;}

        public int TalbePos { get; set; }
        public  string? Email { get; set; }

        public  string? Nickname { get; set; }

        public  int? Avatar { get; set; }
        // Add other properties you want to expose, but exclude the password
    }

}