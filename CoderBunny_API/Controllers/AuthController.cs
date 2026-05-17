using CoderBunny_API1.Data;
using CoderBunny_API1.Models;
using CoderBunny_API1_Updated.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace CoderBunny_API1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AuthController(AppDbContext db)
        {
            _db = db;
        }

        public class SignupRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
        }

        public class LoginRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        [HttpPost("Signup")]
        public IActionResult Signup([FromBody] SignupRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Role))
                return BadRequest("Username, password and role are required");

            bool exists = _db.HostUser.Any(u => u.Username == request.Username);
            if (exists)
                return BadRequest("Username already taken");

            var user = new HostUser
            {
                Username = request.Username.Trim(),
                PasswordHash = HashPassword(request.Password),
                Role = request.Role.Trim().ToLower(),
                CreatedAt = DateTime.Now
            };

            _db.HostUser.Add(user);
            _db.SaveChanges();

            return Ok(new
            {
                message = "Account created successfully",
                hostUserId = user.HostUserId,
                username = user.Username,
                role = user.Role
            });
        }

        [HttpPost("Login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Username and password are required");

            string hash = HashPassword(request.Password);

            var user = _db.HostUser.FirstOrDefault(u =>
                u.Username == request.Username.Trim() &&
                u.PasswordHash == hash);

            if (user == null)
                return Unauthorized("Invalid username or password");

            return Ok(new
            {
                message = "Login successful",
                hostUserId = user.HostUserId,
                username = user.Username,
                role = user.Role
            });
        }

        private static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}