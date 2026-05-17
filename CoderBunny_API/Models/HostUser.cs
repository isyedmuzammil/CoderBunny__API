using System;

namespace CoderBunny_API1_Updated.Models
{
    public partial class HostUser
    {
        public int HostUserId { get; set; }
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string Role { get; set; } = null!;
        public DateTime CreatedAt { get; set; }

    }
}
