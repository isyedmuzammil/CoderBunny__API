using CoderBunny_API1.Data;
using CoderBunny_API1.Hubs;
using CoderBunny_API1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CoderBunny_API1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoomController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<GameHub> _hub;

        public RoomController(AppDbContext context, IHubContext<GameHub> hub)
        {
            _db = context;
            _hub = hub;
        }

        // =====================================================================
        // JOIN ROOM
        // =====================================================================
        [HttpPost("JoinRoom")]
        public IActionResult JoinRoom([FromBody] JoinRoomRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.RoomCode))
                    return BadRequest("Room code required");

                string roomCode = request.RoomCode;

                var game = _db.Game
                    .FirstOrDefault(g => g.RoomCode.ToLower() == roomCode.ToLower());

                if (game == null)
                    return NotFound("Invalid room code");

                if (game.GameStatus == "Started")
                    return BadRequest("Game already started");

                var existingPlayers = _db.GamePlayers
                    .Where(p => p.GameId == game.GameId)
                    .ToList();

                if (existingPlayers.Count >= 4)
                    return BadRequest("Room is full");

                // 🔥 CREATE PLAYER
                var player = new Player
                {
                    PlayerName = "Player_" + Guid.NewGuid().ToString().Substring(0, 5),
                    PlayerImage = "bunny1.png"
                };

                _db.Player.Add(player);
                _db.SaveChanges();

                // 🔥 ADD TO GAMEPLAYERS
                var newPlayer = new GamePlayers
                {
                    GameId = game.GameId,
                    PlayerId = player.PlayerId,
                    CurrentPosition = 0,
                    Direction = "Right"
                };

                _db.GamePlayers.Add(newPlayer);
                _db.SaveChanges();

                // 🔥 PLAYERS LIST
                var players = _db.GamePlayers
                    .Where(p => p.GameId == game.GameId)
                    .Select(p => new
                    {
                        playerId = p.PlayerId,
                        name = p.Player.PlayerName ?? "",
                        image = p.Player.PlayerImage ?? "bunny1.png",
                        position = p.CurrentPosition ?? 0,
                        direction = p.Direction ?? "right",
                        isReady = false
                    })
                    .ToList();

                //// 🔥 SIGNALR (MODERN VERSION)
                //_hub.Clients.Group(roomCode).SendAsync("playersUpdated", players);

                return Ok(new
                {
                    gameId = game.GameId,
                    roomCode = game.RoomCode,
                    playerId = player.PlayerId,
                    players = players
                });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.InnerException?.Message;
                return StatusCode(500, inner ?? ex.Message);
            }
        }
    }

    // =====================================================================
    // REQUEST MODEL
    // =====================================================================
    public class JoinRoomRequest
    {
        public string RoomCode { get; set; } = string.Empty;
    }
}