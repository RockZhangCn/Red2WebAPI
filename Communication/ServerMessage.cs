using System.ComponentModel.DataAnnotations;

namespace Red2WebAPI.Communication {
    public class ServerMessage
    {
        public required string Type { get; set; }
        [Required]
        public required bool Result { get; set; }
        public required string Message { get; set; }

    }
}