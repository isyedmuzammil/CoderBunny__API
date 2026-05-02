using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CoderBunny_API1.Hubs;
using CoderBunny_API1.Data;
using CoderBunny_API1.Models;

namespace CoderBunny_API1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly IHubContext<GameHub> _hub;
        private readonly AppDbContext _db;
        public GameController(AppDbContext db, IHubContext<GameHub> hub)
        {
            _db = db;
            _hub = hub;
        }
        // ─── Thread-safe dice ────────────────────────────────────────────────
        private static readonly Random _rnd = new Random();
        private static readonly object _rndLock = new object();

        private int RollDiceValue()
        {
            lock (_rndLock)
            {
                return _rnd.Next(1, 4); 
            }
        }

        // ─── Safe SignalR broadcast ───────────────────────────────────────────
        private async Task Broadcast(string? roomCode, string method, object data)
        {
            if (_hub == null || string.IsNullOrWhiteSpace(roomCode)) return;

            try
            {
                switch (method)
                {
                    case "gameStarted":
                        await _hub.Clients.Group(roomCode).SendAsync("gameStarted", data); break;
                    case "gamePaused":
                        await _hub.Clients.Group(roomCode).SendAsync("gamePaused", data); break;
                    case "gameResumed":
                        await _hub.Clients.Group(roomCode).SendAsync("gameResumed", data); break;
                    case "GameEnded":
                        await _hub.Clients.Group(roomCode).SendAsync("GameEnded", data); break;
                    case "GameRestarted":
                        await _hub.Clients.Group(roomCode).SendAsync("GameRestarted", data); break;
                    case "gameCompleted":
                        await _hub.Clients.Group(roomCode).SendAsync("gameCompleted", data); break;
                    case "diceRolled":
                        await _hub.Clients.Group(roomCode).SendAsync("diceRolled", data); break;
                    case "playerMoved":
                        await _hub.Clients.Group(roomCode).SendAsync("playerMoved", data); break;
                    case "turnChanged":
                        await _hub.Clients.Group(roomCode).SendAsync("turnChanged", data); break;
                    case "MoveUndone":
                        await _hub.Clients.Group(roomCode).SendAsync("MoveUndone", data); break;
                    case "PlayerJoined":
                        await _hub.Clients.Group(roomCode).SendAsync("PlayerJoined", data); break;
                    case "playersUpdated":
                        await _hub.Clients.Group(roomCode).SendAsync("playersUpdated", data); break;
                }
            }
            catch { /* Never let SignalR crash the API response */ }
        }

        // =====================================================================
        // START GAME
        // =====================================================================
        [HttpPost("StartGame")]
        public async Task<IActionResult> StartGame(
            string difficulty,
            string gameStatus,
            [FromQuery] int[]? playerIds = null)
        {
            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    Game g;
                    List<int> players;

                    // ✅ CASE 1: SOLO / OLD FLOW (playerIds aaye)
                    if (playerIds != null && playerIds.Length > 0)
                    {
                        if (playerIds.Length != playerIds.Distinct().Count())
                            return BadRequest("Player ID duplication conflict");

                        g = new Game
                        {
                            DifficultyLevel = difficulty,
                            NumberOfPlayers = playerIds.Length,
                            GameStatus = gameStatus,
                            RoomCode = GenerateRoomCode()
                        };

                        _db.Game.Add(g);
                        await _db.SaveChangesAsync();

                        players = playerIds.ToList();
                    }
                    else
                    {
                        // ✅ CASE 2: MULTIPLAYER FLOW (DB se players uthao)
                        g = _db.Game
                            .OrderByDescending(x => x.GameId)
                            .FirstOrDefault(x => x.GameStatus == "Waiting");

                        if (g == null)
                            return BadRequest("No waiting game found");

                        players = _db.GamePlayers
                            .Where(p => p.GameId == g.GameId)
                            .Select(p => p.PlayerId)
                            .ToList();

                        if (players == null || players.Count == 0)
                            return BadRequest("No players joined");

                        g.DifficultyLevel = difficulty;
                        g.NumberOfPlayers = players.Count;
                        g.GameStatus = gameStatus;
                    }

                    // ✅ Cards
                    var allCardIds = _db.CardMaster
                                        .Select(c => c.CardId)
                                        .ToList();

                    int order = 1;

                    foreach (int pid in players)
                    {
                        // 👉 Solo case: insert
                        if (playerIds != null && playerIds.Length > 0)
                        {
                            _db.GamePlayers.Add(new GamePlayers
                            {
                                GameId = g.GameId,
                                PlayerId = pid,
                                PlayerOrder = order,
                                CurrentPosition = 64,
                                Direction = "up",
                                IsActive = true,
                                HasEatenCarrot = false
                            });
                        }
                        else
                        {
                            // 👉 Multiplayer: update existing
                            var existingPlayer = _db.GamePlayers
                                .FirstOrDefault(p => p.GameId == g.GameId && p.PlayerId == pid);

                            if (existingPlayer != null)
                            {
                                existingPlayer.PlayerOrder = order;
                                existingPlayer.CurrentPosition = 64;
                                existingPlayer.Direction = "up";
                                existingPlayer.IsActive = true;
                                existingPlayer.HasEatenCarrot = false;
                            }
                        }

                        // ✅ Turn
                        _db.GameTurn.Add(new GameTurn
                        {
                            GameId = g.GameId,
                            CurrentPlayerId = pid,
                            TurnNumber = order
                        });

                        // ✅ Cards assign
                        foreach (int cardId in allCardIds)
                        {
                            _db.PlayerCard.Add(new PlayerCard
                            {
                                PlayerId = pid,
                                GameId = g.GameId,
                                CardId = cardId,
                                Quantity = 10
                            });
                        }

                        order++;
                    }

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await Broadcast(g.RoomCode, "gameStarted", new
                    {
                        gameId = g.GameId,
                        roomCode = g.RoomCode
                    });

                    return Ok(new
                    {
                        message = "Game Started Successfully",
                        gameId = g.GameId,
                        roomCode = g.RoomCode
                    });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // CREATE GAME
        // =====================================================================
        [HttpPost("CreateGame")]
        public async Task<IActionResult> CreateGame()
        {
            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    var game = new Game
                    {
                        DifficultyLevel = null,
                        NumberOfPlayers = 1,
                        GameStatus = "Waiting",
                        RoomCode = GenerateRoomCode()
                    };

                    _db.Game.Add(game);
                    await _db.SaveChangesAsync();

                    // 🔥 CREATE HOST PLAYER (IMPORTANT)
                    var player = new Player
                    {
                        PlayerName = "Host_" + Guid.NewGuid().ToString().Substring(0, 5),
                        PlayerImage = "bunny1.png"
                    };

                    _db.Player.Add(player);
                    await _db.SaveChangesAsync();

                    var gamePlayer = new GamePlayers
                    {
                        GameId = game.GameId,
                        PlayerId = player.PlayerId,
                        CurrentPosition = 0,
                        Direction = "Right"
                    };

                    _db.GamePlayers.Add(gamePlayer);
                    await _db.SaveChangesAsync();

                    // 🔥 FULL PLAYERS LIST
                    var players = _db.GamePlayers
                        .Where(p => p.GameId == game.GameId)
                        .Select(p => new
                        {
                            playerId = p.Player.PlayerId,
                            name = p.Player.PlayerName,
                            image = p.Player.PlayerImage,
                            position = p.CurrentPosition,
                            direction = p.Direction,
                            isReady = false
                        })
                        .ToList();

                    await tx.CommitAsync();

                    // 🔥 SEND SAME EVENT AS JOIN (IMPORTANT)
                    await _hub.Clients.Group(game.RoomCode).SendAsync("playersUpdated", players);

                    return Ok(new
                    {
                        message = "Game Created Successfully",
                        gameId = game.GameId,
                        roomCode = game.RoomCode,
                        playerId = player.PlayerId,
                        players = players
                    });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // join game so other players can join
        [HttpPost("JoinGame")]
        public async Task<IActionResult> JoinGame(string roomCode, int playerId)
        {
            try
            {
                var game = _db.Game.FirstOrDefault(g => g.RoomCode == roomCode);

                if (game == null)
                    return NotFound("Game not found");

                if (game.GameStatus != "Waiting")
                    return BadRequest("Game already started");

                bool alreadyJoined = _db.GamePlayers
                    .Any(p => p.GameId == game.GameId && p.PlayerId == playerId);

                if (alreadyJoined)
                    return BadRequest("Player already joined");

                int playerCount = _db.GamePlayers.Count(p => p.GameId == game.GameId);

                var player = new GamePlayers
                {
                    GameId = game.GameId,
                    PlayerId = playerId,
                    PlayerOrder = playerCount + 1,
                    CurrentPosition = 64,
                    Direction = "up",
                    IsActive = true,
                    HasEatenCarrot = false
                };

                _db.GamePlayers.Add(player);

                // update total players
                game.NumberOfPlayers = playerCount + 1;

                await _db.SaveChangesAsync();

                // 🔥 SignalR notify
                await Broadcast(game.RoomCode, "PlayerJoined", new
                {
                    playerId = playerId,
                    totalPlayers = game.NumberOfPlayers
                });

                return Ok(new
                {
                    message = "Joined Successfully",
                    gameId = game.GameId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
            }
        }

        // =====================================================================
        // GET PAUSED GAMES
        // =====================================================================
        [HttpGet("GetPausedGames")]
        public IActionResult GetPausedGames()
        {
            var games = _db.Game
                .Where(g => g.GameStatus == "gamePaused")
                .Join(_db.GamePlayers,
                    g => g.GameId,
                    gp => gp.GameId,
                    (g, gp) => new
                    {
                        gameId = g.GameId,
                        roomCode = g.RoomCode,
                        playerId = gp.PlayerId
                    })
                .ToList();

            return Ok(games);
        }

        // =====================================================================
        // RESUME GAME
        // =====================================================================
        [HttpPost("ResumeGame")]
        public async Task<IActionResult> ResumeGame(int gameId)
        {
            var game = _db.Game.FirstOrDefault(x => x.GameId == gameId);
            if (game == null)
                return NotFound("Game Not Found");
            if (game.GameStatus == "Completed")
                return BadRequest("Game already ended");

            game.GameStatus = "Running";
            await _db.SaveChangesAsync();

            await Broadcast(game.RoomCode, "gameResumed", new { gameId = game.GameId });
            return Ok("Running");
        }

        // =====================================================================
        // PAUSE GAME
        // =====================================================================
        [HttpPost("PauseGame")]
        public async Task<IActionResult> PauseGame(int gameId)
        {
            var game = _db.Game.FirstOrDefault(x => x.GameId == gameId);
            if (game == null)
                return NotFound("Game Not Found");
            if (game.GameStatus == "Completed")
                return BadRequest("Game already ended");

            game.GameStatus = "gamePaused";
            await _db.SaveChangesAsync();

            await Broadcast(game.RoomCode, "gamePaused", new { gameId = game.GameId });
            return Ok(new { message = "Game Paused" });
        }

        // =====================================================================
        // END GAME
        // =====================================================================
        [HttpPost("EndGame")]
        public async Task<IActionResult> EndGame(int gameId)
        {
            var game = _db.Game.FirstOrDefault(x => x.GameId == gameId);
            if (game == null)
                return NotFound("Game Not Found");

            game.GameStatus = "Completed";
            await _db.SaveChangesAsync();

            await Broadcast(game.RoomCode, "GameEnded", new { gameId = game.GameId });
            return Ok(new { message = "Game Ended Successfully" });
        }

        // =====================================================================
        // RESTART GAME
        // =====================================================================
        [HttpPost("RestartGame")]
        public async Task<IActionResult> RestartGame(int gameId)
        {
            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    var game = _db.Game.FirstOrDefault(g => g.GameId == gameId);
                    if (game == null)
                        return NotFound("Game not found");

                    // 1. Delete card usages for this game's moves
                    var moveIds = _db.GameMove
                                     .Where(m => m.GameId == gameId)
                                     .Select(m => m.MoveId)
                                     .ToList();

                    var cardUsages = _db.PlayerCardUsage
                                        .Where(c => moveIds.Contains(c.MoveId))
                                        .ToList();
                    _db.PlayerCardUsage.RemoveRange(cardUsages);

                    // 2. Delete moves
                    var moves = _db.GameMove.Where(m => m.GameId == gameId).ToList();
                    _db.GameMove.RemoveRange(moves);

                    // 3. Reset player positions
                    var players = _db.GamePlayers
                                      .Where(p => p.GameId == gameId)
                                      .OrderBy(p => p.PlayerOrder)
                                      .ToList();

                    foreach (var player in players)
                    {
                        player.CurrentPosition = 64;
                        player.Direction = "up";
                        player.HasEatenCarrot = false;
                    }

                    // 4. Reset player cards
                    var playerCards = _db.PlayerCard
                                         .Where(pc => pc.GameId == gameId)
                                         .ToList();
                    foreach (var card in playerCards)
                        card.Quantity = 10;

                    // 5. Rebuild turn order from scratch
                    var oldTurns = _db.GameTurn.Where(t => t.GameId == gameId).ToList();
                    _db.GameTurn.RemoveRange(oldTurns);
                    await _db.SaveChangesAsync(); // flush removals before re-inserting

                    int order = 1;
                    foreach (var player in players)
                    {
                        _db.GameTurn.Add(new GameTurn
                        {
                            GameId = gameId,
                            CurrentPlayerId = player.PlayerId,
                            TurnNumber = order++
                        });
                    }

                    // 6. Reset game status
                    game.GameStatus = "Running";

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await Broadcast(game.RoomCode, "GameRestarted", new { gameId = gameId });

                    return Ok(new
                    {
                        message = "Game restarted successfully",
                        gameId = gameId
                    });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // GET GAME STATE
        // =====================================================================
        [HttpGet("GetGameState")]
        public IActionResult GetGameState(int gameId)
        {
            var status = _db.Game
                            .Where(x => x.GameId == gameId)
                            .Select(x => x.GameStatus)
                            .FirstOrDefault();

            if (status == null)
                return NotFound("Game Not Found");

            return Ok(status);
        }

        // =====================================================================
        // ROLL DICE
        // =====================================================================
        [HttpPost("RollDice")]
        public async Task<IActionResult> RollDice([FromBody] GameMoveRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request data.");

            var game = _db.Game.FirstOrDefault(g => g.GameId == request.GameId);
            if (game == null)
                return NotFound("Game not found.");

            if (game.GameStatus != "Running")
                return BadRequest("Game is not currently running.");

            // Verify player is active in this game
            var playerInGame = _db.GamePlayers.FirstOrDefault(gp =>
                gp.GameId == request.GameId &&
                gp.PlayerId == request.PlayerId &&
                gp.IsActive == true);

            if (playerInGame == null)
                return BadRequest("Player does not belong to this game");

            // Enforce turn order
            var currentTurn = _db.GameTurn
                .Where(t => t.GameId == request.GameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn == null)
                return BadRequest("No active turn found.");

            if (currentTurn.CurrentPlayerId != request.PlayerId)
                return BadRequest("Not your turn");

            // Player must have submitted cards for their previous move first
            var lastMove = _db.GameMove
                .Where(m => m.GameId == request.GameId &&
                            m.PlayerId == request.PlayerId)
                .OrderByDescending(m => m.SequenceId)
                .FirstOrDefault();

            if (lastMove != null)
            {
                bool cardsUsed = _db.PlayerCardUsage
                                    .Any(u => u.MoveId == lastMove.MoveId);
                if (!cardsUsed)
                    return BadRequest("You must use cards before rolling dice again");
            }

            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    int diceValue = RollDiceValue();

                    var gameMove = new GameMove
                    {
                        GameId = request.GameId,
                        PlayerId = request.PlayerId,
                        DiceValue = diceValue,
                        SequenceId = GetNextSequenceId(request.GameId, request.PlayerId),
                        MoveTime = DateTime.Now
                    };

                    _db.GameMove.Add(gameMove);
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await Broadcast(game.RoomCode, "diceRolled", new
                    {
                        playerId = request.PlayerId,
                        diceValue = diceValue,
                        moveId = gameMove.MoveId
                    });

                    return Ok(new
                    {
                        gameMove.MoveId,
                        gameMove.DiceValue
                    });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // MOVE PLAYER
        // =====================================================================
        [HttpPost("MovePlayer")]
        public async Task<IActionResult> MovePlayer(int moveId)
        {
            var move = _db.GameMove.FirstOrDefault(m => m.MoveId == moveId);
            if (move == null)
                return NotFound("Move not found");

            var player = _db.GamePlayers.FirstOrDefault(p =>
                p.GameId == move.GameId &&
                p.PlayerId == move.PlayerId &&
                p.IsActive == true);
            if (player == null)
                return NotFound("Player not found");

            var game = _db.Game.FirstOrDefault(g => g.GameId == move.GameId);
            if (game == null)
                return NotFound("Game not found");

            if (game.GameStatus != "Running")
                return BadRequest("Game is not currently running.");

            // Cards must have been submitted for this move
            var usedCards = _db.PlayerCardUsage
                               .Where(u => u.MoveId == moveId)
                               .ToList();

            if (usedCards.Count == 0)
                return BadRequest("No cards used for this move");

            // Safe defaults
            if (player.CurrentPosition == null) player.CurrentPosition = 64;
            if (string.IsNullOrEmpty(player.Direction)) player.Direction = "up";

            int oldPosition = player.CurrentPosition.Value;
            string direction = player.Direction;
            int currentPosition = oldPosition;
            bool blocked = false;

            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    foreach (var card in usedCards)
                    {
                        if (blocked) break;

                        switch (card.CardId)
                        {
                            case 3: // Move 1 step forward
                                {
                                    int next = MoveOneStep(currentPosition, direction);
                                    if (next < 0 || next > 80)
                                    {
                                        blocked = true;
                                        break;
                                    }
                                    if (IsBoardBlocked(move.GameId, next))
                                    {
                                        blocked = true;
                                        break;
                                    }
                                    if (IsTileOccupied(move.GameId, move.PlayerId, next))
                                        return BadRequest("Tile already occupied");

                                    currentPosition = next;
                                    if (currentPosition == 20) player.HasEatenCarrot = true;
                                    break;
                                }

                            case 1: // Move 2 steps forward
                                {
                                    for (int step = 0; step < 2; step++)
                                    {
                                        int next = MoveOneStep(currentPosition, direction);
                                        if (next < 0 || next > 80)
                                        {
                                            blocked = true;
                                            break;
                                        }
                                        if (IsBoardBlocked(move.GameId, next))
                                        {
                                            blocked = true;
                                            break;
                                        }
                                        if (IsTileOccupied(move.GameId, move.PlayerId, next))
                                            return BadRequest("Tile already occupied");

                                        currentPosition = next;
                                        if (currentPosition == 20) player.HasEatenCarrot = true;
                                    }
                                    break;
                                }

                            case 2: // Turn right — no position change
                                direction = TurnRight(direction);
                                continue;

                            case 4: // Turn left — no position change
                                direction = TurnLeft(direction);
                                continue;
                        }
                    }

                    // ── Blocked: persist direction, advance turn, broadcast ──
                    if (blocked)
                    {
                        player.Direction = direction;
                        move.FromX = oldPosition;
                        move.ToX = oldPosition;

                        AdvanceTurn(move.GameId, move.PlayerId);
                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();

                        await Broadcast(game.RoomCode, "playerMoved", new
                        {
                            playerId = player.PlayerId,
                            newPosition = oldPosition,
                            direction = direction,
                            blocked = true
                        });
                        await BroadcastNextTurn(game.RoomCode, move.GameId);

                        return Ok(new
                        {
                            move.MoveId,
                            OldPosition = oldPosition,
                            NewPosition = oldPosition,
                            Direction = direction,
                            Message = "Blocked",
                            HasEatenCarrot = player.HasEatenCarrot,
                            GameStatus = game.GameStatus
                        });
                    }

                    // ── Win condition ─────────────────────────────────────────
                    if (currentPosition == 40)
                    {
                        if (player.HasEatenCarrot != true)
                        {
                            await tx.RollbackAsync();
                            return BadRequest("Eat your carrot before reaching destination");
                        }

                        int totalMoves = _db.GameMove.Count(m =>
                            m.GameId == move.GameId &&
                            m.PlayerId == move.PlayerId);

                        int finishPos =
                            _db.Result.Count(r => r.GameId == move.GameId) + 1;

                        _db.Result.Add(new Result
                        {
                            GameId = move.GameId,
                            PlayerId = player.PlayerId,
                            Position = finishPos,
                            Remarks = finishPos == 1
                                           ? "Winner"
                                           : $"Finished #{finishPos}"
                        });

                        game.GameStatus = "Completed";
                        player.CurrentPosition = currentPosition;
                        player.Direction = direction;
                        move.FromX = oldPosition;
                        move.ToX = currentPosition;

                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();

                        await Broadcast(game.RoomCode, "gameCompleted", new
                        {
                            winnerId = player.PlayerId,
                            gameId = game.GameId,
                            totalMoves = totalMoves
                        });

                        return Ok(new
                        {
                            GameStatus = "Completed",
                            TotalMoves = totalMoves
                        });
                    }

                    // ── Normal move ───────────────────────────────────────────
                    player.CurrentPosition = currentPosition;
                    player.Direction = direction;
                    move.FromX = oldPosition;
                    move.ToX = currentPosition;

                    AdvanceTurn(move.GameId, move.PlayerId);

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await Broadcast(game.RoomCode, "playerMoved", new
                    {
                        playerId = player.PlayerId,
                        newPosition = currentPosition,
                        direction = direction,
                        blocked = false
                    });
                    await BroadcastNextTurn(game.RoomCode, move.GameId);

                    return Ok(new
                    {
                        move.MoveId,
                        OldPosition = oldPosition,
                        NewPosition = currentPosition,
                        Direction = direction,
                        HasEatenCarrot = player.HasEatenCarrot,
                        GameStatus = game.GameStatus
                    });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // UNDO LAST MOVE
        // =====================================================================
        [HttpPost("UndoLastMove")]
        public async Task<IActionResult> UndoLastMove(int gameId, int playerId)
        {
            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    var lastMove = _db.GameMove
                        .Where(m => m.GameId == gameId && m.PlayerId == playerId)
                        .OrderByDescending(m => m.SequenceId)
                        .FirstOrDefault();

                    if (lastMove == null)
                        return BadRequest("No move to undo");

                    var player = _db.GamePlayers.FirstOrDefault(p =>
                        p.GameId == gameId && p.PlayerId == playerId);
                    if (player == null)
                        return NotFound("Player not found");

                    var game = _db.Game.FirstOrDefault(g => g.GameId == gameId);
                    if (game == null)
                        return NotFound("Game not found");

                    // Restore position to before this move
                    int previousPosition = lastMove.FromX ?? player.CurrentPosition ?? 64;
                    player.CurrentPosition = previousPosition;

                    // Remove card usages for this move
                    var usages = _db.PlayerCardUsage
                                    .Where(u => u.MoveId == lastMove.MoveId)
                                    .ToList();
                    _db.PlayerCardUsage.RemoveRange(usages);

                    // Remove the move record itself
                    _db.GameMove.Remove(lastMove);

                    // Restore turn to this player (move them to front)
                    RestoreTurnToPlayer(gameId, playerId);

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await Broadcast(game.RoomCode, "MoveUndone", new
                    {
                        playerId = playerId,
                        newPosition = previousPosition
                    });
                    await BroadcastNextTurn(game.RoomCode, gameId);

                    return Ok(new
                    {
                        message = "Move undone",
                        NewPosition = previousPosition
                    });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // GET PLAYER POSITIONS
        // =====================================================================
        [HttpGet("GetPlayerPositions")]
        public IActionResult GetPlayerPositions(int gameId)
        {
            var players = _db.GamePlayers
                .Where(p => p.GameId == gameId && p.IsActive == true)
                .Select(p => new
                {
                    p.PlayerId,
                    p.CurrentPosition,
                    p.Direction
                })
                .ToList();

            return Ok(players);
        }

        // =====================================================================
        // GET DICE HISTORY BY PLAYER
        // =====================================================================
        [HttpGet("GetDiceByPlayer")]
        public IActionResult GetDiceByPlayer(int playerId, int gameId)
        {
            var list = _db.GameMove
                .Where(x => x.PlayerId == playerId && x.GameId == gameId)
                .OrderBy(x => x.SequenceId)
                .Select(x => new
                {
                    x.GameId,
                    x.SequenceId,
                    x.DiceValue
                })
                .ToList();

            return Ok(list);
        }

        // =====================================================================
        // PING
        // =====================================================================
        [HttpPost("Ping")]
        public IActionResult Ping([FromBody] string name)
        {
            return Ok(name);
        }

        // =====================================================================
        // GET GAME BY ROOM
        // =====================================================================
        [HttpGet("GetGameByRoom")]
        public IActionResult GetGameByRoom(string roomCode)
        {
            var game = _db.Game.FirstOrDefault(g => g.RoomCode == roomCode);

            if (game == null)
                return NotFound();

            return Ok(new
            {
                game.GameId,
                game.RoomCode,
                game.GameStatus
            });
        }

        // =====================================================================
        // GET PLAYERS
        // =====================================================================
        [HttpGet("GetPlayers")]
        public IActionResult GetPlayers(int gameId)
        {
            var players = _db.GamePlayers
                .Where(p => p.GameId == gameId && p.IsActive == true)
                .Select(p => new
                {
                    playerId = p.PlayerId,
                    name = "Player " + p.PlayerId, // temporary
                    currentPosition = p.CurrentPosition,
                    direction = p.Direction
                })
                .ToList();

            return Ok(players);
        }

        // =====================================================================
        // REQUEST MODEL
        // =====================================================================
        public class GameMoveRequest
        {
            public int GameId { get; set; }
            public int PlayerId { get; set; }
        }

        // =====================================================================
        // PRIVATE HELPERS
        // =====================================================================

        private string GenerateRoomCode()
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            lock (_rndLock)
            {
                char first = letters[_rnd.Next(letters.Length)];
                char second = letters[_rnd.Next(letters.Length)];
                int nums = _rnd.Next(1000, 10000);
                return $"{first}{second}{nums}";
            }
        }

        private int GetNextSequenceId(int gameId, int playerId)
        {
            var last = _db.GameMove
                .Where(m => m.GameId == gameId && m.PlayerId == playerId)
                .OrderByDescending(m => m.SequenceId)
                .Select(m => (int?)m.SequenceId)
                .FirstOrDefault();

            return (last ?? 0) + 1;
        }

        /// <summary>
        /// Pushes the current player to the back of the turn queue and
        /// renumbers all turns 1..N to prevent unbounded growth.
        /// </summary>
        private void AdvanceTurn(int gameId, int currentPlayerId)
        {
            var turns = _db.GameTurn
                .Where(t => t.GameId == gameId)
                .OrderBy(t => t.TurnNumber)
                .ToList();

            if (turns.Count == 0) return;

            var currentEntry = turns.FirstOrDefault(
                t => t.CurrentPlayerId == currentPlayerId);
            if (currentEntry == null) return;

            // Push current player to back
            currentEntry.TurnNumber = turns.Max(t => t.TurnNumber) + 1;

            // Re-fetch ordered and renumber 1..N
            var reordered = _db.GameTurn
                .Where(t => t.GameId == gameId)
                .OrderBy(t => t.TurnNumber)
                .ToList();

            int n = 1;
            foreach (var t in reordered) t.TurnNumber = n++;
        }

        /// <summary>
        /// Moves a player's turn entry to the front of the queue (used by Undo).
        /// </summary>
        private void RestoreTurnToPlayer(int gameId, int playerId)
        {
            var turns = _db.GameTurn
                .Where(t => t.GameId == gameId)
                .OrderBy(t => t.TurnNumber)
                .ToList();

            if (turns.Count == 0) return;

            var entry = turns.FirstOrDefault(t => t.CurrentPlayerId == playerId);
            if (entry == null) return;

            entry.TurnNumber = turns.Min(t => t.TurnNumber) - 1;

            // Renumber 1..N
            var reordered = _db.GameTurn
                .Where(t => t.GameId == gameId)
                .OrderBy(t => t.TurnNumber)
                .ToList();

            int n = 1;
            foreach (var t in reordered) t.TurnNumber = n++;
        }

        /// <summary>
        /// Broadcasts turnChanged with the next player's ID.
        /// Must be called AFTER SaveChanges so DB reflects updated order.
        /// </summary>
        private async Task BroadcastNextTurn(string? roomCode, int gameId)
        {
            if (string.IsNullOrWhiteSpace(roomCode))
                return;

            var next = _db.GameTurn
                .Where(t => t.GameId == gameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (next != null)
            {
                await Broadcast(roomCode, "turnChanged", new
                {
                    currentPlayerId = next.CurrentPlayerId
                });
            }
        }

        /// <summary>
        /// Returns true when the board tile at the given flat index is a
        /// blocking asset (Puddle or Fence).
        /// </summary>
        private bool IsBoardBlocked(int gameId, int positionIndex)
        {
            int x = positionIndex % 9;
            int y = positionIndex / 9;

            var tile = _db.BoardConfig.FirstOrDefault(b =>
                b.BoardId == gameId &&
                b.X == x &&
                b.Y == y);

            return tile != null &&
                   (tile.AssetType == "Puddle" || tile.AssetType == "Fence");
        }

        /// <summary>
        /// Returns true when another active player already occupies the tile.
        /// </summary>
        private bool IsTileOccupied(int gameId, int movingPlayerId, int positionIndex)
        {
            return _db.GamePlayers.Any(p =>
                p.GameId == gameId &&
                p.PlayerId != movingPlayerId &&
                p.CurrentPosition == positionIndex &&
                p.IsActive == true);
        }

        private int MoveOneStep(int pos, string direction)
        {
            switch (direction)
            {
                case "up": return pos - 9;
                case "down": return pos + 9;
                case "left": return pos - 1;
                case "right": return pos + 1;
                default: return pos;
            }
        }

        private string TurnRight(string direction)
        {
            switch (direction)
            {
                case "up": return "right";
                case "right": return "down";
                case "down": return "left";
                case "left": return "up";
                default: return direction;
            }
        }

        private string TurnLeft(string direction)
        {
            switch (direction)
            {
                case "up": return "left";
                case "left": return "down";
                case "down": return "right";
                case "right": return "up";
                default: return direction;
            }
        }
    }
}
