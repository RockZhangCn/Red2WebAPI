using System.ComponentModel.DataAnnotations;

namespace Red2WebAPI.Communication {
    public class ClientMessage
    {
        // TAKESEAT, READY, DISPATCH2, SHOT, SKIP,
        [Required]
        public required string Action { get; set; }
        public required int TableIdx { get; set; }
        public required int Pos { get; set; }
        public required int Avatar { get; set; }

        public required string NickName { get; set;}

        public List<int> Cards { get; set; } = new List<int>();
    }

}