using System.ComponentModel.DataAnnotations;

namespace CoderBunny_API1.Models
{
    public class JoinRoomRequest
    {
        [Required]
        public string RoomCode { get; set; } = string.Empty;
        public string PlayerName { get; set; } = "Player";
    }
}