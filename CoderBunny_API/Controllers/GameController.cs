using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
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

        // =====================================================================
        // IN-MEMORY TOSS TRACKER
        // Key: gameId  →  Dictionary<playerId, diceValue>
        // Cleared after toss winner is determined
        // =====================================================================
        private static readonly ConcurrentDictionary<int, Dictionary<int, int>> _tossRolls
            = new ConcurrentDictionary<int, Dictionary<int, int>>();

        // =====================================================================
        // IN-MEMORY CONSECUTIVE SKIP TRACKER
        // =====================================================================
        private static readonly ConcurrentDictionary<string, int> _consecutiveSkips
            = new ConcurrentDictionary<string, int>();

        private static string SkipKey(int gameId, int playerId) => $"{gameId}_{playerId}";

        private int GetConsecutiveSkips(int gameId, int playerId)
            => _consecutiveSkips.GetOrAdd(SkipKey(gameId, playerId), 0);

        private int IncrementConsecutiveSkips(int gameId, int playerId)
        {
            return _consecutiveSkips.AddOrUpdate(
                SkipKey(gameId, playerId),
                1,
                (_, old) => old + 1);
        }

        private void ResetConsecutiveSkips(int gameId, int playerId)
            => _consecutiveSkips.TryRemove(SkipKey(gameId, playerId), out _);

        private void CleanupGameSkipKeys(int gameId)
        {
            var keys = _consecutiveSkips.Keys
                .Where(k => k.StartsWith($"{gameId}_"))
                .ToList();
            foreach (var k in keys)
                _consecutiveSkips.TryRemove(k, out _);
        }

        private int RollDiceValue()
        {
            lock (_rndLock) { return _rnd.Next(1, 4); }
        }

        // ─── Safe SignalR broadcast ───────────────────────────────────────────
        private async Task Broadcast(string? roomCode, string method, object data)
        {
            if (_hub == null || string.IsNullOrWhiteSpace(roomCode)) return;
            roomCode = roomCode?.ToLower().Trim();
            try
            {
                switch (method)
                {
                    case "gameStarted":           await _hub.Clients.Group(roomCode).SendAsync("gameStarted", data);           break;
                    case "gamePaused":            await _hub.Clients.Group(roomCode).SendAsync("gamePaused", data);            break;
                    case "gameResumed":           await _hub.Clients.Group(roomCode).SendAsync("gameResumed", data);           break;
                    case "GameEnded":             await _hub.Clients.Group(roomCode).SendAsync("GameEnded", data);             break;
                    case "GameRestarted":         await _hub.Clients.Group(roomCode).SendAsync("GameRestarted", data);         break;
                    case "gameCompleted":         await _hub.Clients.Group(roomCode).SendAsync("gameCompleted", data);         break;
                    case "diceRolled":            await _hub.Clients.Group(roomCode).SendAsync("diceRolled", data);            break;
                    case "playerMoved":           await _hub.Clients.Group(roomCode).SendAsync("playerMoved", data);           break;
                    case "turnChanged":           await _hub.Clients.Group(roomCode).SendAsync("turnChanged", data);           break;
                    case "MoveUndone":            await _hub.Clients.Group(roomCode).SendAsync("MoveUndone", data);            break;
                    case "PlayerJoined":          await _hub.Clients.Group(roomCode).SendAsync("PlayerJoined", data);          break;
                    case "playersUpdated":        await _hub.Clients.Group(roomCode).SendAsync("playersUpdated", data);        break;
                    case "playerLeft":            await _hub.Clients.Group(roomCode).SendAsync("playerLeft", data);            break;
                    case "turnTimerStarted":      await _hub.Clients.Group(roomCode).SendAsync("turnTimerStarted", data);      break;
                    case "playerAutoSkipped":     await _hub.Clients.Group(roomCode).SendAsync("playerAutoSkipped", data);     break;
                    case "playerRemovedInactive": await _hub.Clients.Group(roomCode).SendAsync("playerRemovedInactive", data); break;
                    // ── TOSS EVENTS ──
                    case "tossStarted":           await _hub.Clients.Group(roomCode).SendAsync("tossStarted", data);           break;
                    case "tossRollResult":        await _hub.Clients.Group(roomCode).SendAsync("tossRollResult", data);        break;
                    case "tossTie":               await _hub.Clients.Group(roomCode).SendAsync("tossTie", data);               break;
                    case "tossWinner":            await _hub.Clients.Group(roomCode).SendAsync("tossWinner", data);            break;
                    case "destinationSelected":   await _hub.Clients.Group(roomCode).SendAsync("destinationSelected", data);   break;
                }
            }
            catch { /* Never let SignalR crash the API response */ }
        }

        // =====================================================================
        // SOLO GAME
        // =====================================================================
        [HttpPost("SoloGame")]
        public async Task<IActionResult> SoloGame(
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

                    if (playerIds != null && playerIds.Length > 0)
                    {
                        if (playerIds.Length != playerIds.Distinct().Count())
                            return BadRequest("Player ID duplication conflict");

                        g = new Game
                        {
                            DifficultyLevel = difficulty,
                            NumberOfPlayers = playerIds.Length,
                            GameStatus      = gameStatus,
                            RoomCode        = GenerateRoomCode(),
                            IsTossPhase     = false,
                            TossRound       = 1,
                            Destination     = "carnival"
                        };

                        _db.Game.Add(g);
                        await _db.SaveChangesAsync();
                        players = playerIds.ToList();
                    }
                    else
                    {
                        g = _db.Game
                            .OrderByDescending(x => x.GameId)
                            .FirstOrDefault(x => x.GameStatus == "Waiting");

                        if (g == null) return BadRequest("No waiting game found");

                        players = _db.GamePlayers
                            .Where(p => p.GameId == g.GameId)
                            .Select(p => p.PlayerId)
                            .ToList();

                        if (players == null || players.Count == 0)
                            return BadRequest("No players joined");

                        g.DifficultyLevel = difficulty;
                        g.NumberOfPlayers = players.Count;
                        g.GameStatus      = gameStatus;
                        g.IsTossPhase     = false;
                        g.TossRound       = 1;
                        g.Destination     = "carnival";
                    }

                    var allCardIds = _db.CardMaster.Select(c => c.CardId).ToList();
                    int order = 1;

                    foreach (int pid in players)
                    {
                        if (playerIds != null && playerIds.Length > 0)
                        {
                            _db.GamePlayers.Add(new GamePlayers
                            {
                                GameId          = g.GameId,
                                PlayerId        = pid,
                                PlayerOrder     = order,
                                CurrentPosition = GetStartingPosition(order),
                                Direction       = GetStartingDirection(order),
                                IsActive        = true,
                                HasEatenCarrot  = false
                            });
                        }
                        else
                        {
                            var ep = _db.GamePlayers.FirstOrDefault(p => p.GameId == g.GameId && p.PlayerId == pid);
                            if (ep != null)
                            {
                                ep.PlayerOrder     = order;
                                ep.CurrentPosition = GetStartingPosition(order);
                                ep.Direction       = GetStartingDirection(order);
                                ep.IsActive        = true;
                                ep.HasEatenCarrot  = false;
                            }
                        }

                        _db.GameTurn.Add(new GameTurn
                        {
                            GameId          = g.GameId,
                            CurrentPlayerId = pid,
                            TurnNumber      = order
                        });

                        foreach (int cardId in allCardIds)
                        {
                            _db.PlayerCard.Add(new PlayerCard
                            {
                                PlayerId = pid,
                                GameId   = g.GameId,
                                CardId   = cardId,
                                Quantity = 10
                            });
                        }

                        order++;
                    }

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await Broadcast(g.RoomCode, "gameStarted", new { gameId = g.GameId, roomCode = g.RoomCode });

                    return Ok(new { message = "Game Started Successfully", gameId = g.GameId, roomCode = g.RoomCode });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // START GAME
        // After StartGame → IsTossPhase = true, normal game begins after destination picked
        // =====================================================================
        [HttpPost("StartGame")]
        public async Task<IActionResult> StartGame(string roomCode)
        {
            roomCode = roomCode?.ToLower().Trim();

            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    var game = _db.Game.FirstOrDefault(g => g.RoomCode == roomCode);

                    if (game == null)         return NotFound("Room not found");
                    if (game.GameStatus == "Running")   return BadRequest("Game already started");
                    if (game.GameStatus == "Completed") return BadRequest("Game already completed");

                    var players = _db.GamePlayers.Where(p => p.GameId == game.GameId).ToList();
                    if (players == null || players.Count == 0)
                        return BadRequest("No players joined this game");

                    var allCardIds = _db.CardMaster.Select(c => c.CardId).ToList();

                    // Remove old turns
                    var existingTurns = _db.GameTurn.Where(t => t.GameId == game.GameId).ToList();
                    _db.GameTurn.RemoveRange(existingTurns);
                    await _db.SaveChangesAsync();

                    game.GameStatus  = "Running";
                    game.NumberOfPlayers = players.Count;
                    // ── TOSS PHASE START ──
                    game.IsTossPhase = true;
                    game.TossRound   = 1;
                    game.Destination = null;  // no destination yet

                    int order = 1;
                    foreach (var player in players)
                    {
                        player.PlayerOrder     = order;
                        player.CurrentPosition = GetStartingPosition(order);
                        player.Direction       = GetStartingDirection(order);
                        player.IsActive        = true;
                        player.HasEatenCarrot  = false;

                        _db.GameTurn.Add(new GameTurn
                        {
                            GameId          = game.GameId,
                            CurrentPlayerId = player.PlayerId,
                            TurnNumber      = order
                        });

                        foreach (int cardId in allCardIds)
                        {
                            bool exists = _db.PlayerCard.Any(pc =>
                                pc.PlayerId == player.PlayerId &&
                                pc.GameId   == game.GameId &&
                                pc.CardId   == cardId);

                            if (!exists)
                            {
                                _db.PlayerCard.Add(new PlayerCard
                                {
                                    PlayerId = player.PlayerId,
                                    GameId   = game.GameId,
                                    CardId   = cardId,
                                    Quantity = 10
                                });
                            }
                        }

                        order++;
                    }

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    // Clear in-memory toss rolls for fresh start
                    _tossRolls.TryRemove(game.GameId, out _);

                    var playerOrderMap = players.ToDictionary(
                        p => p.PlayerId.ToString(),
                        p => (object)(p.PlayerOrder ?? 1)
                    );

                    await Broadcast(game.RoomCode, "gameStarted", new
                    {
                        gameId       = game.GameId,
                        roomCode     = game.RoomCode,
                        playerOrders = playerOrderMap
                    });

                    // ── Broadcast tossStarted so all clients enter toss phase ──
                    var firstTossPlayer = players.OrderBy(p => p.PlayerOrder).FirstOrDefault();
                    await Broadcast(game.RoomCode, "tossStarted", new
                    {
                        tossRound             = 1,
                        pendingPlayerIds      = players.Select(p => p.PlayerId).ToList(),
                        currentTossPlayerId   = firstTossPlayer?.PlayerId,
                        currentTossPlayerOrder = firstTossPlayer?.PlayerOrder ?? 1,
                        message               = "Game started! Roll to determine who picks the destination."
                    });

                    await Broadcast(game.RoomCode, "turnTimerStarted", new
                    {
                        currentPlayerId    = firstTossPlayer?.PlayerId,
                        currentPlayerOrder = firstTossPlayer?.PlayerOrder ?? 1,
                        timerSeconds       = 60
                    });

                    return Ok(new { message = "Game Started Successfully", gameId = game.GameId, roomCode = game.RoomCode });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // TOSS ROLL
        // Each player calls this once during toss phase.
        // When ALL players have rolled → determine winner or handle tie.
        // =====================================================================
        // =====================================================================
        // TOSS ROLL
        // Each player calls this once during toss phase.
        // When ALL players have rolled → determine winner or handle tie.
        // =====================================================================
        [HttpPost("TossRoll")]
        public async Task<IActionResult> TossRoll([FromBody] GameMoveRequest request)
        {
            if (request == null) return BadRequest("Invalid request");

            var game = _db.Game.FirstOrDefault(g => g.GameId == request.GameId);
            if (game == null) return NotFound("Game not found");
            if (!game.IsTossPhase) return BadRequest("Not in toss phase");
            if (game.GameStatus != "Running") return BadRequest("Game is not running");

            var playerInGame = _db.GamePlayers.FirstOrDefault(p =>
                p.GameId == request.GameId &&
                p.PlayerId == request.PlayerId &&
                p.IsActive == true);
            if (playerInGame == null) return BadRequest("Player not in this game");

            var currentTurn = _db.GameTurn
                .Where(t => t.GameId == request.GameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn == null) return BadRequest("No turn found");
            if (currentTurn.CurrentPlayerId != request.PlayerId)
                return BadRequest("Not your toss turn");

            var rolls = _tossRolls.GetOrAdd(request.GameId, _ => new Dictionary<int, int>());

            lock (rolls)
            {
                if (rolls.ContainsKey(request.PlayerId))
                    return BadRequest("You have already rolled in this toss round");
            }

            int diceValue = RollDiceValue();

            var allActivePlayers = _db.GamePlayers
                .Where(p => p.GameId == request.GameId && p.IsActive == true)
                .OrderBy(p => p.PlayerOrder)
                .ToList();

            var playerInfo = _db.Player.FirstOrDefault(p => p.PlayerId == request.PlayerId);
            string playerName = playerInfo?.PlayerName ?? $"Player {playerInGame.PlayerOrder}";

            lock (rolls) { rolls[request.PlayerId] = diceValue; }

            await Broadcast(game.RoomCode, "tossRollResult", new
            {
                playerId = request.PlayerId,
                playerOrder = playerInGame.PlayerOrder ?? 1,
                playerName = playerName,
                diceValue = diceValue,
                tossRound = game.TossRound
            });

            int totalPlayers = allActivePlayers.Count;
            int rolledCount;
            lock (rolls) { rolledCount = rolls.Count; }

            if (rolledCount < totalPlayers)
            {
                // ── NOT ALL ROLLED YET — advance turn to next pending player ──
                // Remove current player from front of queue, push to back
                var existingTurns = _db.GameTurn
                    .Where(t => t.GameId == request.GameId)
                    .OrderBy(t => t.TurnNumber)
                    .ToList();

                var myEntry = existingTurns.FirstOrDefault(t => t.CurrentPlayerId == request.PlayerId);
                if (myEntry != null)
                {
                    _db.GameTurn.Remove(myEntry);
                    await _db.SaveChangesAsync();

                    // Renumber remaining
                    var remaining = _db.GameTurn
                        .Where(t => t.GameId == request.GameId)
                        .OrderBy(t => t.TurnNumber)
                        .ToList();
                    int n = 1;
                    foreach (var t in remaining) t.TurnNumber = n++;

                    // Push current player to end (for tie re-roll fairness)
                    _db.GameTurn.Add(new GameTurn
                    {
                        GameId = request.GameId,
                        CurrentPlayerId = request.PlayerId,
                        TurnNumber = remaining.Count + 1
                    });
                    await _db.SaveChangesAsync();
                }

                var nextTurn = _db.GameTurn
                    .Where(t => t.GameId == request.GameId)
                    .OrderBy(t => t.TurnNumber)
                    .FirstOrDefault();

                if (nextTurn != null)
                {
                    var nextPlayer = _db.GamePlayers
                        .FirstOrDefault(p => p.GameId == request.GameId && p.PlayerId == nextTurn.CurrentPlayerId);

                    await Broadcast(game.RoomCode, "turnChanged", new
                    {
                        currentPlayerId = nextTurn.CurrentPlayerId,
                        currentPlayerOrder = nextPlayer?.PlayerOrder ?? 1,
                        isTossPhase = true
                    });

                    await Broadcast(game.RoomCode, "turnTimerStarted", new
                    {
                        currentPlayerId = nextTurn.CurrentPlayerId,
                        currentPlayerOrder = nextPlayer?.PlayerOrder ?? 1,
                        timerSeconds = 60
                    });
                }

                return Ok(new
                {
                    message = "Roll recorded, waiting for others",
                    diceValue = diceValue,
                    rolled = rolledCount,
                    total = totalPlayers
                });
            }

            // ── ALL players have rolled — determine winner ──
            Dictionary<int, int> finalRolls;
            lock (rolls) { finalRolls = new Dictionary<int, int>(rolls); }

            int maxScore = finalRolls.Values.Max();
            var winners = finalRolls.Where(kv => kv.Value == maxScore).Select(kv => kv.Key).ToList();

            if (winners.Count == 1)
            {
                // ── SINGLE WINNER ──
                int winnerPlayerId = winners[0];
                var winnerPlayer = allActivePlayers.FirstOrDefault(p => p.PlayerId == winnerPlayerId);
                var winnerInfo = _db.Player.FirstOrDefault(p => p.PlayerId == winnerPlayerId);
                string winnerName = winnerInfo?.PlayerName ?? $"Player {winnerPlayer?.PlayerOrder}";

                _tossRolls.TryRemove(request.GameId, out _);

                // ── KEY FIX: Directly rebuild turn queue with ONLY winner at front ──
                // Do NOT use AdvanceTurn/RestoreTurnToPlayer — they have SaveChanges conflicts
                var oldTurns = _db.GameTurn.Where(t => t.GameId == request.GameId).ToList();
                _db.GameTurn.RemoveRange(oldTurns);
                await _db.SaveChangesAsync();

                // Winner goes FIRST — this is what SelectDestination validates
                _db.GameTurn.Add(new GameTurn
                {
                    GameId = request.GameId,
                    CurrentPlayerId = winnerPlayerId,
                    TurnNumber = 1
                });

                // Add remaining players after winner (they'll be used after toss ends)
                int turnOrder = 2;
                foreach (var ap in allActivePlayers)
                {
                    if (ap.PlayerId == winnerPlayerId) continue;
                    _db.GameTurn.Add(new GameTurn
                    {
                        GameId = request.GameId,
                        CurrentPlayerId = ap.PlayerId,
                        TurnNumber = turnOrder++
                    });
                }

                await _db.SaveChangesAsync();

                await Broadcast(game.RoomCode, "tossWinner", new
                {
                    winnerPlayerId = winnerPlayerId,
                    winnerPlayerOrder = winnerPlayer?.PlayerOrder ?? 1,
                    winnerPlayerName = winnerName,
                    winnerScore = maxScore,
                    allScores = finalRolls.Select(kv =>
                    {
                        var ap = allActivePlayers.FirstOrDefault(p => p.PlayerId == kv.Key);
                        var pi = _db.Player.FirstOrDefault(p => p.PlayerId == kv.Key);
                        return new
                        {
                            playerId = kv.Key,
                            playerOrder = ap?.PlayerOrder ?? 1,
                            playerName = pi?.PlayerName ?? $"Player {ap?.PlayerOrder}",
                            score = kv.Value
                        };
                    }).ToList(),
                    message = $"{winnerName} won the toss with {maxScore}! Pick the destination."
                });

                return Ok(new
                {
                    message = "Toss complete — winner determined",
                    diceValue = diceValue,
                    winnerId = winnerPlayerId,
                    isTie = false
                });
            }
            else
            {
                // ── TIE — re-roll among tied players only ──
                game.TossRound += 1;
                await _db.SaveChangesAsync();

                _tossRolls.TryRemove(request.GameId, out _);

                // Rebuild turn queue with ONLY tied players
                var oldTurns = _db.GameTurn.Where(t => t.GameId == request.GameId).ToList();
                _db.GameTurn.RemoveRange(oldTurns);
                await _db.SaveChangesAsync();

                var tiedPlayers = allActivePlayers.Where(p => winners.Contains(p.PlayerId)).ToList();
                int tieOrder = 1;
                foreach (var tp in tiedPlayers)
                {
                    _db.GameTurn.Add(new GameTurn
                    {
                        GameId = request.GameId,
                        CurrentPlayerId = tp.PlayerId,
                        TurnNumber = tieOrder++
                    });
                }
                await _db.SaveChangesAsync();

                var firstTied = tiedPlayers.OrderBy(p => p.PlayerOrder).FirstOrDefault();

                await Broadcast(game.RoomCode, "tossTie", new
                {
                    tossRound = game.TossRound,
                    tiedPlayerIds = winners,
                    currentTossPlayerId = firstTied?.PlayerId,
                    currentTossPlayerOrder = firstTied?.PlayerOrder ?? 1,
                    allScores = finalRolls.Select(kv =>
                    {
                        var ap = allActivePlayers.FirstOrDefault(p => p.PlayerId == kv.Key);
                        var pi = _db.Player.FirstOrDefault(p => p.PlayerId == kv.Key);
                        return new
                        {
                            playerId = kv.Key,
                            playerOrder = ap?.PlayerOrder ?? 1,
                            playerName = pi?.PlayerName ?? $"Player {ap?.PlayerOrder}",
                            score = kv.Value
                        };
                    }).ToList(),
                    message = $"Tie at {maxScore}! Tied players roll again."
                });

                if (firstTied != null)
                {
                    await Broadcast(game.RoomCode, "turnTimerStarted", new
                    {
                        currentPlayerId = firstTied.PlayerId,
                        currentPlayerOrder = firstTied.PlayerOrder ?? 1,
                        timerSeconds = 60
                    });
                }

                return Ok(new
                {
                    message = "Tie! Tied players must re-roll",
                    diceValue = diceValue,
                    isTie = true,
                    tiedIds = winners
                });
            }
        }
        // =====================================================================
        // SELECT DESTINATION
        // Called by toss winner — picks the destination for the game.
        // After this, IsTossPhase = false and normal game begins.
        // =====================================================================
        [HttpPost("SelectDestination")]
        public async Task<IActionResult> SelectDestination([FromBody] SelectDestinationRequest request)
        {
            if (request == null) return BadRequest("Invalid request");

            var validDestinations = new HashSet<string> { "carnival", "zoo", "park", "school" };
            if (string.IsNullOrEmpty(request.Destination) || !validDestinations.Contains(request.Destination.ToLower()))
                return BadRequest("Invalid destination. Must be: carnival, zoo, park, school");

            var game = _db.Game.FirstOrDefault(g => g.GameId == request.GameId);
            if (game == null)      return NotFound("Game not found");
            if (!game.IsTossPhase) return BadRequest("Not in toss phase");

            // Validate it is the requesting player's turn (they are the toss winner)
            var currentTurn = _db.GameTurn
                .Where(t => t.GameId == request.GameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn == null) return BadRequest("No turn found");
            if (currentTurn.CurrentPlayerId != request.PlayerId)
                return BadRequest("It's not your turn to select destination");

            // ── Save destination and end toss phase ──
            game.Destination = request.Destination.ToLower();
            game.IsTossPhase = false;
            game.TossRound   = 1;

            // Rebuild full turn queue with all active players for normal game
            var existingTurns = _db.GameTurn.Where(t => t.GameId == request.GameId).ToList();
            _db.GameTurn.RemoveRange(existingTurns);
            await _db.SaveChangesAsync();

            var allPlayers = _db.GamePlayers
                .Where(p => p.GameId == request.GameId && p.IsActive == true)
                .OrderBy(p => p.PlayerOrder)
                .ToList();

            int ord = 1;
            foreach (var p in allPlayers)
            {
                _db.GameTurn.Add(new GameTurn
                {
                    GameId          = request.GameId,
                    CurrentPlayerId = p.PlayerId,
                    TurnNumber      = ord++
                });
            }

            await _db.SaveChangesAsync();

            var firstPlayer = allPlayers.FirstOrDefault();
            var winnerPlayer = _db.Player.FirstOrDefault(p => p.PlayerId == request.PlayerId);
            var winnerGamePlayer = allPlayers.FirstOrDefault(p => p.PlayerId == request.PlayerId);

            // Map destination to position on board
            int destPosition = GetDestinationPosition(game.Destination);

            await Broadcast(game.RoomCode, "destinationSelected", new
            {
                destination        = game.Destination,
                destinationPosition = destPosition,
                selectedByPlayerId = request.PlayerId,
                selectedByName     = winnerPlayer?.PlayerName ?? $"Player {winnerGamePlayer?.PlayerOrder}",
                currentPlayerId    = firstPlayer?.PlayerId,
                currentPlayerOrder = firstPlayer?.PlayerOrder ?? 1,
                message            = $"{winnerPlayer?.PlayerName} chose {game.Destination}! Game starts now!"
            });

            await Broadcast(game.RoomCode, "turnTimerStarted", new
            {
                currentPlayerId    = firstPlayer?.PlayerId,
                currentPlayerOrder = firstPlayer?.PlayerOrder ?? 1,
                timerSeconds       = 60
            });

            return Ok(new
            {
                message             = "Destination selected, game starts!",
                destination         = game.Destination,
                destinationPosition = destPosition
            });
        }

        // =====================================================================
        // GET CURRENT TURN
        // Now returns isTossPhase flag so frontend knows which mode it's in
        // =====================================================================
        [HttpGet("GetCurrentTurn")]
        public IActionResult GetCurrentTurn(int gameId)
        {
            var currentTurn = _db.GameTurn
                .AsNoTracking()
                .Where(t => t.GameId == gameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn == null) return NotFound("No turn found");

            var player = _db.GamePlayers
                .AsNoTracking()
                .FirstOrDefault(p => p.GameId == gameId && p.PlayerId == currentTurn.CurrentPlayerId);

            var game = _db.Game.AsNoTracking().FirstOrDefault(g => g.GameId == gameId);

            return Ok(new
            {
                currentPlayerId    = currentTurn.CurrentPlayerId,
                currentPlayerOrder = player?.PlayerOrder ?? 1,
                isTossPhase        = game?.IsTossPhase ?? false,
                destination        = game?.Destination
            });
        }

        // =====================================================================
        // CREATE GAME
        // =====================================================================
        [HttpPost("CreateGame")]
        public async Task<IActionResult> CreateGame(string playerName = "Host")
        {
            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    var game = new Game
                    {
                        DifficultyLevel = null,
                        NumberOfPlayers = 1,
                        GameStatus      = "Waiting",
                        RoomCode        = GenerateRoomCode(),
                        IsTossPhase     = false,
                        TossRound       = 1,
                        Destination     = null
                    };

                    _db.Game.Add(game);
                    await _db.SaveChangesAsync();

                    var player = new Player { PlayerName = playerName, PlayerImage = "bunny1.png" };
                    _db.Player.Add(player);
                    await _db.SaveChangesAsync();

                    var gamePlayer = new GamePlayers
                    {
                        GameId          = game.GameId,
                        PlayerId        = player.PlayerId,
                        CurrentPosition = 0,
                        Direction       = "Right"
                    };

                    _db.GamePlayers.Add(gamePlayer);
                    await _db.SaveChangesAsync();

                    var players = _db.GamePlayers
                        .Where(p => p.GameId == game.GameId)
                        .Select(p => new
                        {
                            playerId  = p.Player.PlayerId,
                            name      = p.Player.PlayerName,
                            image     = p.Player.PlayerImage,
                            position  = p.CurrentPosition,
                            direction = p.Direction,
                            isReady   = false
                        })
                        .ToList();

                    await tx.CommitAsync();
                    await _hub.Clients.Group(game.RoomCode).SendAsync("playersUpdated", players);

                    return Ok(new
                    {
                        message  = "Game Created Successfully",
                        gameId   = game.GameId,
                        roomCode = game.RoomCode,
                        playerId = player.PlayerId,
                        players  = players,
                        isHost   = true
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
        // JOIN GAME
        // =====================================================================
        [HttpPost("JoinGame")]
        public async Task<IActionResult> JoinGame(string roomCode, int playerId)
        {
            try
            {
                var game = _db.Game.FirstOrDefault(g => g.RoomCode == roomCode);
                if (game == null)               return NotFound("Game not found");
                if (game.GameStatus != "Waiting") return BadRequest("Game already started");

                bool alreadyJoined = _db.GamePlayers.Any(p => p.GameId == game.GameId && p.PlayerId == playerId);
                if (alreadyJoined) return BadRequest("Player already joined");

                int playerCount = _db.GamePlayers.Count(p => p.GameId == game.GameId);
                if (playerCount >= 4) return BadRequest("Room is full — maximum 4 players allowed");

                var player = new GamePlayers
                {
                    GameId          = game.GameId,
                    PlayerId        = playerId,
                    PlayerOrder     = playerCount + 1,
                    CurrentPosition = 64,
                    Direction       = "up",
                    IsActive        = true,
                    HasEatenCarrot  = false
                };

                _db.GamePlayers.Add(player);
                game.NumberOfPlayers = playerCount + 1;
                await _db.SaveChangesAsync();

                await Broadcast(game.RoomCode, "PlayerJoined", new { playerId, totalPlayers = game.NumberOfPlayers });

                return Ok(new { message = "Joined Successfully", gameId = game.GameId });
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
                .Select(g => new
                {
                    gameId   = g.GameId,
                    roomCode = g.RoomCode,
                    players  = _db.GamePlayers
                        .Where(p => p.GameId == g.GameId && p.IsActive == true)
                        .Select(p => new
                        {
                            playerId    = p.PlayerId,
                            playerOrder = p.PlayerOrder,
                            playerName  = p.Player.PlayerName
                        })
                        .ToList()
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
            if (game == null)                    return NotFound("Game Not Found");
            if (game.GameStatus == "Completed")  return BadRequest("Game already ended");

            game.GameStatus = "Running";
            await _db.SaveChangesAsync();

            var players = _db.GamePlayers.Where(p => p.GameId == gameId && p.IsActive == true).ToList();
            var playerOrderMap = players.ToDictionary(p => p.PlayerId.ToString(), p => (object)(p.PlayerOrder ?? 1));

            await Broadcast(game.RoomCode, "gameResumed", new
            {
                gameId       = game.GameId,
                roomCode     = game.RoomCode,
                playerOrders = playerOrderMap
            });

            var currentTurn = _db.GameTurn
                .Where(t => t.GameId == gameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn != null)
            {
                var cp = _db.GamePlayers.FirstOrDefault(p => p.GameId == gameId && p.PlayerId == currentTurn.CurrentPlayerId);
                await Broadcast(game.RoomCode, "turnTimerStarted", new
                {
                    currentPlayerId    = currentTurn.CurrentPlayerId,
                    currentPlayerOrder = cp?.PlayerOrder ?? 1,
                    timerSeconds       = 60
                });
            }

            return Ok(new { message = "Game Resumed", gameId = game.GameId, roomCode = game.RoomCode });
        }

        // =====================================================================
        // PAUSE GAME
        // =====================================================================
        [HttpPost("PauseGame")]
        public async Task<IActionResult> PauseGame(int gameId)
        {
            var game = _db.Game.FirstOrDefault(x => x.GameId == gameId);
            if (game == null)                    return NotFound("Game Not Found");
            if (game.GameStatus == "Completed")  return BadRequest("Game already ended");

            game.GameStatus = "gamePaused";
            await _db.SaveChangesAsync();

            await Broadcast(game.RoomCode, "gamePaused", new { gameId = game.GameId });
            return Ok(new { message = "Game Paused" });
        }

        // =====================================================================
        // END GAME
        // =====================================================================
        [HttpPost("EndGame")]
        public async Task<IActionResult> EndGame(int gameId, int playerId)
        {
            var game = _db.Game.FirstOrDefault(x => x.GameId == gameId);
            if (game == null) return NotFound("Game Not Found");

            var leavingPlayer = _db.GamePlayers.FirstOrDefault(p => p.GameId == gameId && p.PlayerId == playerId);
            if (leavingPlayer != null) leavingPlayer.IsActive = false;

            var leavingPlayerInfo = _db.Player.FirstOrDefault(p => p.PlayerId == playerId);
            var activePlayers     = _db.GamePlayers.Where(p => p.GameId == gameId && p.IsActive == true).ToList();

            ResetConsecutiveSkips(gameId, playerId);

            if (activePlayers.Count <= 1)
            {
                game.GameStatus = "Completed";
                await _db.SaveChangesAsync();
                CleanupGameSkipKeys(gameId);

                await Broadcast(game.RoomCode, "playerLeft", new
                {
                    playerId       = playerId,
                    playerName     = leavingPlayerInfo?.PlayerName ?? "A player",
                    remainingPlayers = activePlayers.Count
                });
                await Broadcast(game.RoomCode, "GameEnded", new { gameId = game.GameId });
            }
            else
            {
                var playerTurn = _db.GameTurn.FirstOrDefault(t => t.GameId == gameId && t.CurrentPlayerId == playerId);
                if (playerTurn != null) _db.GameTurn.Remove(playerTurn);

                var remaining = _db.GameTurn.Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).ToList();
                int n = 1;
                foreach (var t in remaining) t.TurnNumber = n++;

                await _db.SaveChangesAsync();

                await Broadcast(game.RoomCode, "playerLeft", new
                {
                    playerId       = playerId,
                    playerName     = leavingPlayerInfo?.PlayerName ?? "A player",
                    remainingPlayers = activePlayers.Count
                });

                await BroadcastNextTurn(game.RoomCode, gameId);
            }

            return Ok(new { message = "Left Successfully" });
        }

        // =====================================================================
        // RESTART GAME
        // Restart puts the game back into toss phase
        // =====================================================================
        [HttpPost("RestartGame")]
        public async Task<IActionResult> RestartGame(int gameId)
        {
            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    var game = _db.Game.FirstOrDefault(g => g.GameId == gameId);
                    if (game == null) return NotFound("Game not found");

                    var moveIds   = _db.GameMove.Where(m => m.GameId == gameId).Select(m => m.MoveId).ToList();
                    var cardUsages = _db.PlayerCardUsage.Where(c => moveIds.Contains(c.MoveId)).ToList();
                    _db.PlayerCardUsage.RemoveRange(cardUsages);

                    var moves = _db.GameMove.Where(m => m.GameId == gameId).ToList();
                    _db.GameMove.RemoveRange(moves);

                    var players = _db.GamePlayers.Where(p => p.GameId == gameId).OrderBy(p => p.PlayerOrder).ToList();
                    int order = 1;
                    foreach (var player in players)
                    {
                        player.CurrentPosition = GetStartingPosition(order);
                        player.Direction       = GetStartingDirection(order);
                        player.HasEatenCarrot  = false;
                        order++;
                    }

                    var playerCards = _db.PlayerCard.Where(pc => pc.GameId == gameId).ToList();
                    foreach (var card in playerCards) card.Quantity = 10;

                    var oldTurns = _db.GameTurn.Where(t => t.GameId == gameId).ToList();
                    _db.GameTurn.RemoveRange(oldTurns);
                    await _db.SaveChangesAsync();

                    order = 1;
                    foreach (var player in players)
                    {
                        _db.GameTurn.Add(new GameTurn
                        {
                            GameId          = gameId,
                            CurrentPlayerId = player.PlayerId,
                            TurnNumber      = order++
                        });
                    }

                    // ── Reset game to toss phase ──
                    game.GameStatus  = "Running";
                    game.IsTossPhase = true;
                    game.TossRound   = 1;
                    game.Destination = null;

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    CleanupGameSkipKeys(gameId);
                    _tossRolls.TryRemove(gameId, out _);

                    await Broadcast(game.RoomCode, "GameRestarted", new
                    {
                        gameId      = gameId,
                        isTossPhase = true
                    });

                    var firstPlayer = players.OrderBy(p => p.PlayerOrder).FirstOrDefault();
                    if (firstPlayer != null)
                    {
                        await Broadcast(game.RoomCode, "tossStarted", new
                        {
                            tossRound              = 1,
                            pendingPlayerIds       = players.Select(p => p.PlayerId).ToList(),
                            currentTossPlayerId    = firstPlayer.PlayerId,
                            currentTossPlayerOrder = firstPlayer.PlayerOrder ?? 1,
                            message                = "Game restarted! Roll to determine destination picker."
                        });

                        await Broadcast(game.RoomCode, "turnTimerStarted", new
                        {
                            currentPlayerId    = firstPlayer.PlayerId,
                            currentPlayerOrder = firstPlayer.PlayerOrder ?? 1,
                            timerSeconds       = 60
                        });
                    }

                    return Ok(new { message = "Game restarted successfully", gameId = gameId });
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
            var game = _db.Game.Where(x => x.GameId == gameId).FirstOrDefault();
            if (game == null) return NotFound("Game Not Found");

            return Ok(new
            {
                gameStatus  = game.GameStatus,
                isTossPhase = game.IsTossPhase,
                destination = game.Destination
            });
        }

        // =====================================================================
        // ROLL DICE
        // Blocked during toss phase — players must use TossRoll instead
        // =====================================================================
        [HttpPost("RollDice")]
        public async Task<IActionResult> RollDice([FromBody] GameMoveRequest request)
        {
            if (request == null) return BadRequest("Invalid request data.");

            var game = _db.Game.FirstOrDefault(g => g.GameId == request.GameId);
            if (game == null)                    return NotFound("Game not found.");
            if (game.GameStatus != "Running")    return BadRequest("Game is not currently running.");
            if (game.IsTossPhase)                return BadRequest("Toss phase active — use TossRoll endpoint");

            var playerInGame = _db.GamePlayers.FirstOrDefault(gp =>
                gp.GameId   == request.GameId &&
                gp.PlayerId == request.PlayerId &&
                gp.IsActive == true);
            if (playerInGame == null) return BadRequest("Player does not belong to this game");

            var currentTurn = _db.GameTurn
                .Where(t => t.GameId == request.GameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn == null)                         return BadRequest("No active turn found.");
            if (currentTurn.CurrentPlayerId != request.PlayerId) return BadRequest("Not your turn");

            var lastMove = _db.GameMove
                .Where(m => m.GameId == request.GameId && m.PlayerId == request.PlayerId)
                .OrderByDescending(m => m.SequenceId)
                .FirstOrDefault();

            if (lastMove != null)
            {
                bool cardsUsed = _db.PlayerCardUsage.Any(u => u.MoveId == lastMove.MoveId);
                if (!cardsUsed) return BadRequest("You must use cards before rolling dice again");
            }

            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    int diceValue = RollDiceValue();

                    var gameMove = new GameMove
                    {
                        GameId     = request.GameId,
                        PlayerId   = request.PlayerId,
                        DiceValue  = diceValue,
                        SequenceId = GetNextSequenceId(request.GameId, request.PlayerId),
                        MoveTime   = DateTime.Now
                    };

                    _db.GameMove.Add(gameMove);
                    await _db.SaveChangesAsync();

                    playerInGame.BugUsedInCurrentMove = false;
                    await _db.SaveChangesAsync();

                    ResetConsecutiveSkips(request.GameId, request.PlayerId);

                    await tx.CommitAsync();

                    await Broadcast(game.RoomCode, "diceRolled", new
                    {
                        playerId  = request.PlayerId,
                        diceValue = diceValue,
                        moveId    = gameMove.MoveId
                    });

                    return Ok(new { gameMove.MoveId, gameMove.DiceValue });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // AUTO SKIP TURN
        // =====================================================================
        [HttpPost("AutoSkipTurn")]
        public async Task<IActionResult> AutoSkipTurn([FromBody] AutoSkipRequest request)
        {
            if (request == null) return BadRequest("Invalid request");

            var game = _db.Game.FirstOrDefault(g => g.GameId == request.GameId);
            if (game == null)                 return NotFound("Game not found");
            if (game.GameStatus != "Running") return Ok(new { message = "Game not running, skip ignored" });

            var currentTurn = _db.GameTurn
                .Where(t => t.GameId == request.GameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn == null)                               return BadRequest("No active turn");
            if (currentTurn.CurrentPlayerId != request.PlayerId)   return Ok(new { message = "No longer this player's turn" });

            var player = _db.GamePlayers.FirstOrDefault(p =>
                p.GameId   == request.GameId &&
                p.PlayerId == request.PlayerId &&
                p.IsActive == true);

            if (player == null) return BadRequest("Player not found or already inactive");

            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    // Clean up dangling dice roll
                    var pendingMove = _db.GameMove
                        .Where(m => m.GameId == request.GameId && m.PlayerId == request.PlayerId)
                        .OrderByDescending(m => m.SequenceId)
                        .FirstOrDefault();

                    if (pendingMove != null)
                    {
                        bool cardsUsed = _db.PlayerCardUsage.Any(u => u.MoveId == pendingMove.MoveId);
                        if (!cardsUsed)
                        {
                            _db.GameMove.Remove(pendingMove);
                            await _db.SaveChangesAsync();
                        }
                    }

                    // During toss phase — also clear any partial toss roll
                    if (game.IsTossPhase)
                    {
                        // Remove from toss rolls if present
                        if (_tossRolls.TryGetValue(request.GameId, out var rolls))
                        {
                            lock (rolls) { rolls.Remove(request.PlayerId); }
                        }
                    }

                    int newSkipCount = IncrementConsecutiveSkips(request.GameId, request.PlayerId);
                    var playerInfo   = _db.Player.FirstOrDefault(p => p.PlayerId == request.PlayerId);
                    string playerName = playerInfo?.PlayerName ?? $"Player {player.PlayerOrder}";

                    if (newSkipCount >= 3)
                    {
                        player.IsActive = false;

                        var playerTurnEntry = _db.GameTurn
                            .FirstOrDefault(t => t.GameId == request.GameId && t.CurrentPlayerId == request.PlayerId);
                        if (playerTurnEntry != null) _db.GameTurn.Remove(playerTurnEntry);

                        var remainingTurns = _db.GameTurn
                            .Where(t => t.GameId == request.GameId)
                            .OrderBy(t => t.TurnNumber)
                            .ToList();
                        int n = 1;
                        foreach (var t in remainingTurns) t.TurnNumber = n++;

                        await _db.SaveChangesAsync();

                        var remainingActive = _db.GamePlayers
                            .AsNoTracking()
                            .Where(p => p.GameId == request.GameId && p.IsActive == true)
                            .ToList();

                        ResetConsecutiveSkips(request.GameId, request.PlayerId);

                        await tx.CommitAsync();

                        await Broadcast(game.RoomCode, "playerRemovedInactive", new
                        {
                            playerId         = request.PlayerId,
                            playerOrder      = player.PlayerOrder ?? 1,
                            playerName       = playerName,
                            reason           = "3 consecutive turns skipped",
                            remainingPlayers = remainingActive.Count
                        });

                        if (remainingActive.Count <= 1)
                        {
                            game.GameStatus = "Completed";
                            var winner = remainingActive.FirstOrDefault();
                            if (winner != null)
                            {
                                bool alreadyHasResult = _db.Result.Any(r => r.GameId == request.GameId && r.PlayerId == winner.PlayerId);
                                if (!alreadyHasResult)
                                {
                                    int nextPos = _db.Result.Count(r => r.GameId == request.GameId) + 1;
                                    _db.Result.Add(new Result
                                    {
                                        GameId   = request.GameId,
                                        PlayerId = winner.PlayerId,
                                        Position = nextPos,
                                        Remarks  = nextPos == 1 ? "Winner" : $"Finished #{nextPos}"
                                    });
                                }
                            }
                            await _db.SaveChangesAsync();
                            CleanupGameSkipKeys(request.GameId);

                            var finalResults = _db.Result
                                .Where(r => r.GameId == request.GameId)
                                .OrderBy(r => r.Position)
                                .Select(r => new
                                {
                                    playerId    = r.PlayerId,
                                    position    = r.Position,
                                    remarks     = r.Remarks,
                                    totalMoves  = _db.GameMove.Count(m => m.GameId == request.GameId && m.PlayerId == r.PlayerId),
                                    playerOrder = _db.GamePlayers.Where(gp => gp.GameId == request.GameId && gp.PlayerId == r.PlayerId).Select(gp => gp.PlayerOrder).FirstOrDefault(),
                                    playerName  = _db.Player.Where(p => p.PlayerId == r.PlayerId).Select(p => p.PlayerName).FirstOrDefault()
                                }).ToList();

                            await Broadcast(game.RoomCode, "gameCompleted", new { gameId = request.GameId, results = finalResults });
                        }
                        else
                        {
                            // If still in toss phase and player removed — continue toss with remaining
                            if (game.IsTossPhase)
                            {
                                await BroadcastNextTossTurn(game.RoomCode, request.GameId);
                            }
                            else
                            {
                                await BroadcastNextTurn(game.RoomCode, request.GameId);
                                await BroadcastTurnTimer(game.RoomCode, request.GameId);
                            }
                        }

                        return Ok(new { message = "Player removed due to inactivity", playerId = request.PlayerId, consecutiveSkips = newSkipCount, playerRemoved = true });
                    }
                    else
                    {
                        AdvanceTurn(request.GameId, request.PlayerId);
                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();

                        await Broadcast(game.RoomCode, "playerAutoSkipped", new
                        {
                            skippedPlayerId            = request.PlayerId,
                            skippedPlayerOrder         = player.PlayerOrder ?? 1,
                            skippedPlayerName          = playerName,
                            consecutiveSkips           = newSkipCount,
                            skipsRemainingBeforeRemoval = 3 - newSkipCount,
                            isTossPhase                = game.IsTossPhase
                        });

                        if (game.IsTossPhase)
                        {
                            await BroadcastNextTossTurn(game.RoomCode, request.GameId);
                        }
                        else
                        {
                            await BroadcastNextTurn(game.RoomCode, request.GameId);
                            await BroadcastTurnTimer(game.RoomCode, request.GameId);
                        }

                        return Ok(new { message = "Turn skipped", playerId = request.PlayerId, consecutiveSkips = newSkipCount, playerRemoved = false });
                    }
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, ex.InnerException?.InnerException?.Message ?? ex.Message);
                }
            }
        }

        // =====================================================================
        // HELPERS
        // =====================================================================
        private int GetStartingPosition(int playerOrder)
        {
            return playerOrder switch { 1 => 64, 2 => 16, 3 => 70, 4 => 10, _ => 64 };
        }

        private string GetStartingDirection(int playerOrder)
        {
            return playerOrder switch { 1 => "up", 2 => "down", 3 => "up", 4 => "down", _ => "up" };
        }

        private int GetCarrotPosition(int playerOrder)
        {
            return playerOrder switch { 1 => 24, 2 => 60, 3 => 56, 4 => 20, _ => 20 };
        }

        // ── Maps destination key to board position (flat index) ──
        private int GetDestinationPosition(string destination)
        {
            return destination?.ToLower() switch
            {
                "carnival" => 40,
                "zoo"      => 13,
                "park"     => 58,
                "school"   => 31,
                _          => 40
            };
        }

        // ── LOOP PREPROCESSOR ──
        private static readonly HashSet<int> LoopCardIds = new HashSet<int> { 5, 6, 7 };
        private static readonly Dictionary<int, int> LoopCardValues = new Dictionary<int, int>
        {
            { 5, 2 }, { 6, 3 }, { 7, 4 },
        };

        private List<int> ExpandLoopCards(List<int> originalCardIds)
        {
            if (!originalCardIds.Any(id => LoopCardIds.Contains(id))) return originalCardIds;

            var expanded = new List<int>();
            int i = 0;
            while (i < originalCardIds.Count)
            {
                int currentId = originalCardIds[i];
                if (LoopCardIds.Contains(currentId))
                {
                    int loopValue = LoopCardValues[currentId];
                    if (i + 1 >= originalCardIds.Count) { i++; continue; }
                    int nextActionId = originalCardIds[i + 1];
                    if (LoopCardIds.Contains(nextActionId)) { i += 2; continue; }
                    for (int repeat = 0; repeat < loopValue; repeat++) expanded.Add(nextActionId);
                    i += 2;
                }
                else { expanded.Add(currentId); i++; }
            }
            return expanded;
        }

        // =====================================================================
        // MOVE PLAYER
        // Blocked during toss phase
        // =====================================================================
        [HttpPost("MovePlayer")]
        public async Task<IActionResult> MovePlayer(int moveId)
        {
            var move = _db.GameMove.FirstOrDefault(m => m.MoveId == moveId);
            if (move == null) return NotFound("Move not found");

            var player = _db.GamePlayers.FirstOrDefault(p =>
                p.GameId   == move.GameId &&
                p.PlayerId == move.PlayerId &&
                p.IsActive == true);
            if (player == null) return NotFound("Player not found");

            var game = _db.Game.FirstOrDefault(g => g.GameId == move.GameId);
            if (game == null)                 return NotFound("Game not found");
            if (game.GameStatus != "Running") return BadRequest("Game is not currently running.");
            if (game.IsTossPhase)             return BadRequest("Toss phase active — complete toss first");

            var usedCardsRaw = _db.PlayerCardUsage.Where(u => u.MoveId == moveId).ToList();
            if (usedCardsRaw.Count == 0) return BadRequest("No cards used for this move");

            if (player.CurrentPosition == null) player.CurrentPosition = 64;
            if (string.IsNullOrEmpty(player.Direction)) player.Direction = "up";

            int oldPosition     = player.CurrentPosition.Value;
            string direction    = player.Direction;
            int currentPosition = oldPosition;
            bool blocked        = false;

            // Destination position — dynamic based on game's chosen destination
            int destinationTileIndex = GetDestinationPosition(game.Destination ?? "carnival");

            var rawCardIds      = usedCardsRaw.Select(c => c.CardId).ToList();
            var expandedCardIds = ExpandLoopCards(rawCardIds);
            var usedCards       = expandedCardIds.Select(id => new PlayerCardUsage { CardId = id }).ToList();

            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    foreach (var card in usedCards)
                    {
                        if (blocked) break;

                        switch (card.CardId)
                        {
                            case 3:
                            {
                                int next = MoveOneStep(currentPosition, direction);
                                if (next < 0 || next > 80) { blocked = true; break; }
                                if (IsBoardBlocked(move.GameId, next)) { blocked = true; move.FromX = oldPosition; move.ToX = oldPosition; break; }
                                if (IsTileOccupied(move.GameId, move.PlayerId, next)) return BadRequest("Tile already occupied");
                                currentPosition = next;
                                int myCarrotPos = GetCarrotPosition(player.PlayerOrder ?? 1);
                                if (currentPosition == myCarrotPos) player.HasEatenCarrot = true;
                                break;
                            }
                            case 1:
                            {
                                for (int step = 0; step < 2; step++)
                                {
                                    int next = MoveOneStep(currentPosition, direction);
                                    if (next < 0 || next > 80) { blocked = true; break; }
                                    if (IsBoardBlocked(move.GameId, next)) { blocked = true; move.FromX = oldPosition; move.ToX = oldPosition; break; }
                                    if (IsTileOccupied(move.GameId, move.PlayerId, next)) return BadRequest("Tile already occupied");
                                    currentPosition = next;
                                    int myCarrotPos = GetCarrotPosition(player.PlayerOrder ?? 1);
                                    if (currentPosition == myCarrotPos) player.HasEatenCarrot = true;
                                }
                                break;
                            }
                            case 2: direction = TurnRight(direction); continue;
                            case 4: direction = TurnLeft(direction);  continue;
                            default: continue;
                        }
                    }

                    if (blocked)
                    {
                        player.Direction = direction;
                        move.FromX       = oldPosition;
                        move.ToX         = oldPosition;

                        await _db.SaveChangesAsync();
                        ResetConsecutiveSkips(move.GameId, move.PlayerId);
                        AdvanceTurn(move.GameId, move.PlayerId);
                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();

                        await Broadcast(game.RoomCode, "playerMoved", new { playerId = player.PlayerId, newPosition = oldPosition, direction, blocked = true });
                        await BroadcastNextTurn(game.RoomCode, move.GameId);
                        await BroadcastTurnTimer(game.RoomCode, move.GameId);

                        return Ok(new
                        {
                            move.MoveId,
                            OldPosition      = oldPosition,
                            NewPosition      = oldPosition,
                            Direction        = direction,
                            Message          = "Blocked",
                            BlockedByHurdle  = blocked,
                            BlockedByBoundary = (currentPosition < 0 || currentPosition > 80),
                            HasEatenCarrot   = player.HasEatenCarrot,
                            GameStatus       = game.GameStatus
                        });
                    }

                    // ── Win condition: dynamic destination tile ──
                    if (currentPosition == destinationTileIndex)
                    {
                        if (player.HasEatenCarrot != true)
                        {
                            await tx.RollbackAsync();
                            return BadRequest("Eat your carrot before reaching destination");
                        }

                        int totalMoves = _db.GameMove.Count(m => m.GameId == move.GameId && m.PlayerId == move.PlayerId);
                        int finishPos  = _db.Result.Count(r => r.GameId == move.GameId) + 1;

                        player.IsFinished       = true;
                        player.FinishPosition   = finishPos;
                        player.CurrentPosition  = currentPosition;
                        player.Direction        = direction;

                        _db.Result.Add(new Result
                        {
                            GameId   = move.GameId,
                            PlayerId = player.PlayerId,
                            Position = finishPos,
                            Remarks  = finishPos == 1 ? "Winner" : $"Finished #{finishPos}"
                        });

                        move.FromX = oldPosition;
                        move.ToX   = currentPosition;

                        ResetConsecutiveSkips(move.GameId, move.PlayerId);
                        AdvanceTurn(move.GameId, move.PlayerId);

                        var allPlayers  = _db.GamePlayers.Where(p => p.GameId == move.GameId && p.IsActive == true).ToList();
                        bool allFinished = allPlayers.All(p => p.IsFinished);

                        if (allFinished)
                        {
                            game.GameStatus = "Completed";
                            await _db.SaveChangesAsync();
                            await tx.CommitAsync();
                            CleanupGameSkipKeys(move.GameId);

                            var finalResults = _db.Result
                                .Where(r => r.GameId == move.GameId)
                                .OrderBy(r => r.Position)
                                .Select(r => new
                                {
                                    playerId    = r.PlayerId,
                                    position    = r.Position,
                                    remarks     = r.Remarks,
                                    totalMoves  = _db.GameMove.Count(m => m.GameId == move.GameId && m.PlayerId == r.PlayerId),
                                    playerOrder = _db.GamePlayers.Where(gp => gp.GameId == move.GameId && gp.PlayerId == r.PlayerId).Select(gp => gp.PlayerOrder).FirstOrDefault(),
                                    playerName  = _db.Player.Where(p => p.PlayerId == r.PlayerId).Select(p => p.PlayerName).FirstOrDefault()
                                }).ToList();

                            await Broadcast(game.RoomCode, "gameCompleted", new { gameId = game.GameId, results = finalResults });
                            return Ok(new { GameStatus = "Completed", TotalMoves = totalMoves, FinishPosition = finishPos });
                        }

                        int initialPosition = GetStartingPosition(player.PlayerOrder ?? 1);
                        player.CurrentPosition = initialPosition;
                        player.Direction       = GetStartingDirection(player.PlayerOrder ?? 1);

                        AdvanceTurn(move.GameId, move.PlayerId);
                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();

                        await Broadcast(game.RoomCode, "playerFinished", new { playerId = player.PlayerId, finishPosition = finishPos, totalMoves, newPosition = initialPosition });
                        await Broadcast(game.RoomCode, "playerMoved", new { playerId = player.PlayerId, newPosition = initialPosition, direction = GetStartingDirection(player.PlayerOrder ?? 1), blocked = false });
                        await BroadcastNextTurn(game.RoomCode, move.GameId);
                        await BroadcastTurnTimer(game.RoomCode, move.GameId);

                        return Ok(new { GameStatus = "PlayerFinished", TotalMoves = totalMoves, FinishPosition = finishPos, NewPosition = initialPosition });
                    }

                    player.CurrentPosition = currentPosition;
                    player.Direction       = direction;
                    move.FromX             = oldPosition;
                    move.ToX               = currentPosition;

                    await _db.SaveChangesAsync();
                    ResetConsecutiveSkips(move.GameId, move.PlayerId);
                    AdvanceTurn(move.GameId, move.PlayerId);
                    await tx.CommitAsync();

                    await Broadcast(game.RoomCode, "playerMoved", new { playerId = player.PlayerId, newPosition = currentPosition, direction, blocked = false });
                    await BroadcastNextTurn(game.RoomCode, move.GameId);
                    await BroadcastTurnTimer(game.RoomCode, move.GameId);

                    return Ok(new
                    {
                        move.MoveId,
                        OldPosition    = oldPosition,
                        NewPosition    = currentPosition,
                        Direction      = direction,
                        HasEatenCarrot = player.HasEatenCarrot,
                        GameStatus     = game.GameStatus
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
        // GET GAME REPLAY
        // =====================================================================
        [HttpGet("GetGameReplay")]
        public IActionResult GetGameReplay(string roomCode)
        {
            roomCode = roomCode?.Trim().ToUpper();

            var game = _db.Game.FirstOrDefault(g => g.RoomCode != null && g.RoomCode.ToUpper() == roomCode);
            if (game == null) return NotFound("Game not found for this room code");
            if (game.GameStatus != "Completed" && game.GameStatus != "gamePaused")
                return BadRequest("Replay only available for completed or paused games");

            var gamePlayers = _db.GamePlayers
                .Where(p => p.GameId == game.GameId)
                .OrderBy(p => p.PlayerOrder)
                .Select(p => new { playerId = p.PlayerId, playerOrder = p.PlayerOrder ?? 1, playerName = p.Player.PlayerName ?? $"Player {p.PlayerOrder}" })
                .ToList();

            var startingPositions  = gamePlayers.ToDictionary(p => p.playerId, p => GetStartingPosition(p.playerOrder));
            var startingDirections = gamePlayers.ToDictionary(p => p.playerId, p => GetStartingDirection(p.playerOrder));

            var finishedPlayerResetPos = new Dictionary<int, int>();
            var resultEntries = _db.Result.Where(r => r.GameId == game.GameId).ToList();
            foreach (var r in resultEntries)
            {
                if (r.PlayerId.HasValue)
                {
                    var gp = gamePlayers.FirstOrDefault(p => p.playerId == r.PlayerId.Value);
                    if (gp != null) finishedPlayerResetPos[r.PlayerId.Value] = GetStartingPosition(gp.playerOrder);
                }
            }

            var allMoves     = _db.GameMove.Where(m => m.GameId == game.GameId).OrderBy(m => m.MoveId).ToList();
            var allMoveIds   = allMoves.Select(m => m.MoveId).ToList();
            var cardsByMove  = _db.PlayerCardUsage
                .Where(u => allMoveIds.Contains(u.MoveId))
                .Join(_db.CardMaster, u => u.CardId, c => c.CardId, (u, c) => new { u.MoveId, u.CardId, CardName = c.CardName })
                .GroupBy(x => x.MoveId)
                .ToDictionary(g => g.Key, g => g.Select(x => new { x.CardId, x.CardName }).ToList());

            var currentPositions  = new Dictionary<int, int>(startingPositions);
            var currentDirections = new Dictionary<int, string>(startingDirections);
            var playerAlreadyShownAtDest = new HashSet<int>();
            var snapshots = new List<object>();

            snapshots.Add(new
            {
                stepIndex = 0, moveId = (int?)null, playerId = (int?)null, playerOrder = (int?)null,
                playerName = (string?)null, diceValue = (int?)null, cardsUsed = new List<object>(),
                boardState = BuildBoardState(currentPositions, currentDirections, playerAlreadyShownAtDest, finishedPlayerResetPos, gamePlayers, -1),
                isInitialState = true, playerFinished = false
            });

            int stepIdx = 1;
            foreach (var move in allMoves)
            {
                int fromPos = move.FromX ?? currentPositions.GetValueOrDefault(move.PlayerId, 64);
                int toPos   = move.ToX   ?? fromPos;
                var cards   = cardsByMove.ContainsKey(move.MoveId)
                    ? cardsByMove[move.MoveId].Select(c => (object)new { c.CardId, c.CardName }).ToList()
                    : new List<object>();
                var pInfo   = gamePlayers.FirstOrDefault(p => p.playerId == move.PlayerId);

                string dir = currentDirections.GetValueOrDefault(move.PlayerId, "up");
                if (toPos != fromPos)
                {
                    int delta = toPos - fromPos;
                    if (delta <= -9) dir = "up"; else if (delta >= 9) dir = "down";
                    else if (delta < 0) dir = "left"; else dir = "right";
                }

                bool isFinishMove = toPos == GetDestinationPosition(game.Destination ?? "carnival")
                                    && finishedPlayerResetPos.ContainsKey(move.PlayerId)
                                    && !playerAlreadyShownAtDest.Contains(move.PlayerId);

                if (isFinishMove) { currentPositions[move.PlayerId] = toPos; currentDirections[move.PlayerId] = dir; }
                else if (playerAlreadyShownAtDest.Contains(move.PlayerId)) { currentPositions[move.PlayerId] = finishedPlayerResetPos[move.PlayerId]; currentDirections[move.PlayerId] = GetStartingDirection(pInfo?.playerOrder ?? 1); }
                else { currentPositions[move.PlayerId] = toPos; currentDirections[move.PlayerId] = dir; }

                snapshots.Add(new
                {
                    stepIndex = stepIdx, moveId = (int?)move.MoveId, playerId = (int?)move.PlayerId,
                    playerOrder = (int?)(pInfo?.playerOrder ?? 1), playerName = pInfo?.playerName ?? $"Player {move.PlayerId}",
                    diceValue = (int?)move.DiceValue, cardsUsed = cards,
                    boardState = BuildBoardState(currentPositions, currentDirections, playerAlreadyShownAtDest, finishedPlayerResetPos, gamePlayers, move.PlayerId),
                    isInitialState = false, playerFinished = isFinishMove
                });

                if (isFinishMove) playerAlreadyShownAtDest.Add(move.PlayerId);
                stepIdx++;
            }

            var results = _db.Result.Where(r => r.GameId == game.GameId).OrderBy(r => r.Position)
                .Select(r => new
                {
                    playerId   = r.PlayerId, position = r.Position, remarks = r.Remarks,
                    totalMoves = _db.GameMove.Count(m => m.GameId == game.GameId && m.PlayerId == r.PlayerId),
                    playerName = _db.Player.Where(p => p.PlayerId == r.PlayerId).Select(p => p.PlayerName).FirstOrDefault()
                }).ToList();

            var boardConfig = _db.BoardConfig.Where(b => b.BoardId == 1)
                .Select(b => new { assetType = b.AssetType ?? "", x = b.X, y = b.Y }).ToList();

            return Ok(new
            {
                gameId = game.GameId, roomCode = game.RoomCode, gameStatus = game.GameStatus,
                destination = game.Destination, players = gamePlayers, totalSteps = snapshots.Count,
                steps = snapshots, results, boardConfig
            });
        }

        private List<object> BuildBoardState(
            Dictionary<int, int> currentPositions, Dictionary<int, string> currentDirections,
            HashSet<int> alreadyShownAtDest, Dictionary<int, int> finishedResetPos,
            IEnumerable<dynamic> gamePlayers, int currentMovingPlayerId)
        {
            return currentPositions.Select(kv =>
            {
                int displayPos    = kv.Value;
                string displayDir = currentDirections.ContainsKey(kv.Key) ? currentDirections[kv.Key] : "up";
                if (kv.Key != currentMovingPlayerId && alreadyShownAtDest.Contains(kv.Key) && finishedResetPos.ContainsKey(kv.Key))
                {
                    displayPos = finishedResetPos[kv.Key];
                    var gp = gamePlayers.FirstOrDefault(p => p.playerId == kv.Key);
                    displayDir = GetStartingDirection(gp?.playerOrder ?? 1);
                }
                return (object)new { playerId = kv.Key, currentPosition = displayPos, direction = displayDir };
            }).ToList();
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
                    var player = _db.GamePlayers.FirstOrDefault(p => p.GameId == gameId && p.PlayerId == playerId);
                    if (player == null) return NotFound("Player not found");

                    if (player.BugUsesRemaining <= 0)
                        return BadRequest(new { message = "Bug limit reached. You can only use bug 3 times per game." });
                    if (player.BugUsedInCurrentMove)
                        return BadRequest(new { message = "Bug already used for this move. Only 1 bug per move allowed." });

                    var lastMove = _db.GameMove
                        .Where(m => m.GameId == gameId && m.PlayerId == playerId)
                        .OrderByDescending(m => m.SequenceId)
                        .FirstOrDefault();
                    if (lastMove == null) return BadRequest(new { message = "No move to undo" });

                    var game = _db.Game.FirstOrDefault(g => g.GameId == gameId);
                    if (game == null) return NotFound("Game not found");

                    int previousPosition = lastMove.FromX ?? player.CurrentPosition ?? 64;
                    player.CurrentPosition    = previousPosition;
                    player.BugUsedInCurrentMove = true;
                    player.BugUsesRemaining    -= 1;

                    var usages = _db.PlayerCardUsage.Where(u => u.MoveId == lastMove.MoveId).ToList();
                    _db.PlayerCardUsage.RemoveRange(usages);
                    _db.GameMove.Remove(lastMove);

                    RestoreTurnToPlayer(gameId, playerId);
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await Broadcast(game.RoomCode, "MoveUndone", new { playerId, newPosition = previousPosition, bugUsesRemaining = player.BugUsesRemaining });
                    await BroadcastNextTurn(game.RoomCode, gameId);
                    await BroadcastTurnTimer(game.RoomCode, gameId);

                    return Ok(new { message = "Move undone", NewPosition = previousPosition, BugUsesRemaining = player.BugUsesRemaining });
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
                .Select(p => new { p.PlayerId, p.CurrentPosition, p.Direction, p.PlayerOrder, PlayerName = p.Player.PlayerName })
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
                .Select(x => new { x.GameId, x.SequenceId, x.DiceValue })
                .ToList();
            return Ok(list);
        }

        // =====================================================================
        // PING
        // =====================================================================
        [HttpPost("Ping")]
        public IActionResult Ping([FromBody] string name) => Ok(name);

        // =====================================================================
        // GET GAME BY ROOM
        // =====================================================================
        [HttpGet("GetGameByRoom")]
        public IActionResult GetGameByRoom(string roomCode)
        {
            var game = _db.Game.FirstOrDefault(g => g.RoomCode == roomCode);
            if (game == null) return NotFound();
            return Ok(new { game.GameId, game.RoomCode, game.GameStatus, game.IsTossPhase, game.Destination });
        }

        // =====================================================================
        // GET PLAYERS
        // =====================================================================
        [HttpGet("GetPlayers")]
        public IActionResult GetPlayers(int gameId)
        {
            var players = _db.GamePlayers
                .Where(p => p.GameId == gameId && p.IsActive == true)
                .Select(p => new { playerId = p.PlayerId, name = "Player " + p.PlayerId, currentPosition = p.CurrentPosition, direction = p.Direction })
                .ToList();
            return Ok(players);
        }

        // =====================================================================
        // REQUEST MODELS
        // =====================================================================
        public class GameMoveRequest
        {
            public int GameId   { get; set; }
            public int PlayerId { get; set; }
        }

        public class AutoSkipRequest
        {
            public int GameId   { get; set; }
            public int PlayerId { get; set; }
        }

        public class SelectDestinationRequest
        {
            public int    GameId      { get; set; }
            public int    PlayerId    { get; set; }
            public string Destination { get; set; } = string.Empty;
        }

        // =====================================================================
        // PRIVATE HELPERS
        // =====================================================================
        private string GenerateRoomCode()
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            lock (_rndLock)
            {
                char first  = letters[_rnd.Next(letters.Length)];
                char second = letters[_rnd.Next(letters.Length)];
                int nums    = _rnd.Next(1000, 10000);
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

        private void AdvanceTurn(int gameId, int currentPlayerId)
        {
            var turns = _db.GameTurn.Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).ToList();
            if (turns.Count == 0) return;

            var currentEntry = turns.FirstOrDefault(t => t.CurrentPlayerId == currentPlayerId);
            if (currentEntry == null) return;

            var currentPlayer = _db.GamePlayers.FirstOrDefault(p => p.GameId == gameId && p.PlayerId == currentPlayerId);

            if (currentPlayer != null && currentPlayer.IsFinished)
            {
                _db.GameTurn.Remove(currentEntry);
            }
            else
            {
                _db.GameTurn.Remove(currentEntry);
                _db.SaveChanges();

                var remaining = _db.GameTurn.Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).ToList();
                int n = 1;
                foreach (var t in remaining) t.TurnNumber = n++;

                _db.GameTurn.Add(new GameTurn
                {
                    GameId          = gameId,
                    CurrentPlayerId = currentPlayerId,
                    TurnNumber      = remaining.Count + 1
                });
            }

            _db.SaveChanges();
            var final = _db.GameTurn.Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).ToList();
            int i = 1;
            foreach (var t in final) t.TurnNumber = i++;
        }

        private void RestoreTurnToPlayer(int gameId, int playerId)
        {
            var turns = _db.GameTurn.Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).ToList();
            if (turns.Count == 0) return;

            var entry = turns.FirstOrDefault(t => t.CurrentPlayerId == playerId);
            if (entry == null) return;

            entry.TurnNumber = turns.Min(t => t.TurnNumber) - 1;

            var reordered = _db.GameTurn.Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).ToList();
            int n = 1;
            foreach (var t in reordered) t.TurnNumber = n++;
        }

        private async Task BroadcastNextTurn(string? roomCode, int gameId)
        {
            if (string.IsNullOrWhiteSpace(roomCode)) return;

            var next = _db.GameTurn.AsNoTracking().Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).FirstOrDefault();
            if (next != null)
            {
                var nextPlayer = _db.GamePlayers.AsNoTracking().FirstOrDefault(p => p.GameId == gameId && p.PlayerId == next.CurrentPlayerId);
                var game       = _db.Game.AsNoTracking().FirstOrDefault(g => g.GameId == gameId);

                await Broadcast(roomCode, "turnChanged", new
                {
                    currentPlayerId    = next.CurrentPlayerId,
                    currentPlayerOrder = nextPlayer?.PlayerOrder ?? 1,
                    isTossPhase        = game?.IsTossPhase ?? false
                });
            }
        }

        private async Task BroadcastTurnTimer(string? roomCode, int gameId)
        {
            if (string.IsNullOrWhiteSpace(roomCode)) return;

            var next = _db.GameTurn.AsNoTracking().Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).FirstOrDefault();
            if (next != null)
            {
                var nextPlayer = _db.GamePlayers.AsNoTracking().FirstOrDefault(p => p.GameId == gameId && p.PlayerId == next.CurrentPlayerId);
                await Broadcast(roomCode, "turnTimerStarted", new
                {
                    currentPlayerId    = next.CurrentPlayerId,
                    currentPlayerOrder = nextPlayer?.PlayerOrder ?? 1,
                    timerSeconds       = 60
                });
            }
        }

        // ── Broadcasts tossStarted with updated next toss player after skip/remove ──
        private async Task BroadcastNextTossTurn(string? roomCode, int gameId)
        {
            if (string.IsNullOrWhiteSpace(roomCode)) return;

            var next = _db.GameTurn.AsNoTracking().Where(t => t.GameId == gameId).OrderBy(t => t.TurnNumber).FirstOrDefault();
            if (next == null) return;

            var game       = _db.Game.AsNoTracking().FirstOrDefault(g => g.GameId == gameId);
            var nextPlayer = _db.GamePlayers.AsNoTracking().FirstOrDefault(p => p.GameId == gameId && p.PlayerId == next.CurrentPlayerId);

            var allActive     = _db.GamePlayers.AsNoTracking().Where(p => p.GameId == gameId && p.IsActive == true).Select(p => p.PlayerId).ToList();
            var alreadyRolled = _tossRolls.TryGetValue(gameId, out var rolls)
                ? rolls.Keys.ToList()
                : new List<int>();
            var pending = allActive.Except(alreadyRolled).ToList();

            await Broadcast(roomCode, "turnChanged", new
            {
                currentPlayerId    = next.CurrentPlayerId,
                currentPlayerOrder = nextPlayer?.PlayerOrder ?? 1,
                isTossPhase        = true
            });

            await Broadcast(roomCode, "turnTimerStarted", new
            {
                currentPlayerId    = next.CurrentPlayerId,
                currentPlayerOrder = nextPlayer?.PlayerOrder ?? 1,
                timerSeconds       = 60
            });
        }

        private bool IsBoardBlocked(int gameId, int positionIndex)
        {
            var hurdlePositions = new HashSet<int>
            {
                1, 2, 3, 4, 5, 6, 7, 9, 17, 18, 26, 27,
                30, 32, 35, 36, 44, 45, 48, 50, 53, 54,
                62, 63, 71, 73, 74, 75, 76, 77, 78, 79
            };
            return hurdlePositions.Contains(positionIndex);
        }

        private bool IsTileOccupied(int gameId, int movingPlayerId, int positionIndex)
        {
            return _db.GamePlayers.Any(p =>
                p.GameId   == gameId &&
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
