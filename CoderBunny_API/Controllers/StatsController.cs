using CoderBunny_API1.Data;
using CoderBunny_API1.Models;
using CoderBunny_API1_Updated.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoderBunny_API1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public StatsController(AppDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        // CARD ID CONSTANTS (mirror GameController)
        // 1=Jump, 2=TurnRight, 3=Forward, 4=TurnLeft, 5=Loop2, 6=Loop3, 7=Loop4
        // =====================================================================
        private static readonly HashSet<int> LoopCardIds = new() { 5, 6, 7 };
        private static readonly HashSet<int> FunctionCardIds = new() { };   // tracked via IsFunction flag
        private static readonly HashSet<int> JumpCardIds = new() { 1 };
        private static readonly HashSet<int> ForwardCardIds = new() { 3 };
        private static readonly HashSet<int> TurnCardIds = new() { 2, 4 };

        // =====================================================================
        // OPTIMAL MOVES CONSTANT
        // Shortest theoretical path on your 9x9 board = 14 moves
        // (start corner → carrot → centre, minimum turns & forwards)
        // Adjust this if your board layout changes.
        // =====================================================================
        private const int OptimalMovesConstant = 14;

        // =====================================================================
        // GET GAME STATS BY ROOM CODE
        // Called from: WatchResults screen (enter room code → see stats)
        // =====================================================================
        [HttpGet("GetStatsByRoom")]
        public IActionResult GetStatsByRoom(string roomCode)
        {
            if (string.IsNullOrWhiteSpace(roomCode))
                return BadRequest("Room code required");

            roomCode = roomCode.Trim().ToUpper();

            var game = _db.Game
                .AsNoTracking()
                .FirstOrDefault(g => g.RoomCode.ToUpper() == roomCode);

            if (game == null)
                return NotFound("Game not found for this room code");

            if (game.GameStatus != "Completed")
                return BadRequest("Game is not yet completed. Stats are only available after game ends.");

            return GetStatsByGameIdInternal(game.GameId);
        }

        // =====================================================================
        // GET GAME STATS BY GAME ID
        // =====================================================================
        [HttpGet("GetStatsByGameId")]
        public IActionResult GetStatsByGameId(int gameId)
        {
            var game = _db.Game.AsNoTracking().FirstOrDefault(g => g.GameId == gameId);
            if (game == null)
                return NotFound("Game not found");

            return GetStatsByGameIdInternal(gameId);
        }

        // =====================================================================
        // INTERNAL: BUILD STATS RESPONSE
        // First checks GameStats table (pre-computed).
        // If not found → computes on-the-fly and saves to DB.
        // =====================================================================
        private IActionResult GetStatsByGameIdInternal(int gameId)
        {
            var game = _db.Game.AsNoTracking().FirstOrDefault(g => g.GameId == gameId);
            if (game == null) return NotFound("Game not found");

            // ── Check pre-computed stats ─────────────────────────────────────
            var existingStats = _db.GameStats
                .AsNoTracking()
                .Where(s => s.GameId == gameId)
                .ToList();

            if (existingStats.Any())
            {
                return Ok(BuildResponse(game, existingStats));
            }

            // ── Compute and persist ──────────────────────────────────────────
            var computed = ComputeAndSaveStats(gameId, game);
            if (computed == null)
                return NotFound("No player data found for this game");

            return Ok(computed);
        }

        // =====================================================================
        // COMPUTE STATS — called once per game, result is saved to DB
        // =====================================================================
        private object? ComputeAndSaveStats(int gameId, Game game)
        {
            var players = _db.GamePlayers
                .Where(p => p.GameId == gameId)
                .ToList();

            if (!players.Any()) return null;

            var results = _db.Result
                .Where(r => r.GameId == gameId)
                .ToList();

            var allMoves = _db.GameMove
                .Where(m => m.GameId == gameId)
                .ToList();

            var allCardUsages = _db.PlayerCardUsage
                .Where(u => u.GameId == gameId)
                .ToList();

            var allFunctionUsages = _db.PlayerFunctionCards
                .Where(f => f.GameId == gameId)
                .ToList();

            var statsList = new List<GameStats>();

            foreach (var player in players)
            {
                var playerMoves = allMoves
                    .Where(m => m.PlayerId == player.PlayerId)
                    .ToList();

                var playerCards = allCardUsages
                    .Where(u => u.PlayerId == player.PlayerId)
                    .ToList();

                var playerResult = results
                    .FirstOrDefault(r => r.PlayerId == player.PlayerId);

                int totalMoves = playerMoves.Count;

                // ── Card counts ──────────────────────────────────────────────
                int loopCount = playerCards.Count(c => LoopCardIds.Contains(c.CardId));
                int jumpCount = playerCards.Count(c => JumpCardIds.Contains(c.CardId));
                int forwardCount = playerCards.Count(c => ForwardCardIds.Contains(c.CardId));
                int turnCount = playerCards.Count(c => TurnCardIds.Contains(c.CardId));
                int functionCount = playerCards.Count(c => c.IsFunction == true);
                int bugCount = player.BugUsesRemaining >= 0
                                    ? (3 - player.BugUsesRemaining)
                                    : 0;

                // ── Time taken ───────────────────────────────────────────────
                int? timeTakenSeconds = null;
                if (playerMoves.Any())
                {
                    var firstMove = playerMoves.Min(m => m.MoveTime);
                    var lastMove = playerMoves.Max(m => m.MoveTime);
                    if (firstMove.HasValue && lastMove.HasValue)
                        timeTakenSeconds = (int)(lastMove.Value - firstMove.Value).TotalSeconds;
                }

                // ── Scores ───────────────────────────────────────────────────
                int efficiencyScore = CalculateEfficiencyScore(totalMoves, OptimalMovesConstant);
                int speedScore = CalculateSpeedScore(timeTakenSeconds);
                int logicScore = CalculateLogicScore(loopCount, functionCount, totalMoves);

                var stat = new GameStats
                {
                    GameId = gameId,
                    PlayerId = player.PlayerId,
                    PlayerOrder = player.PlayerOrder,
                    FinishPosition = playerResult?.Position,
                    TimeTakenSeconds = timeTakenSeconds,
                    TotalMoves = totalMoves,
                    OptimalMoves = OptimalMovesConstant,
                    LoopUsedCount = loopCount,
                    FunctionUsedCount = functionCount,
                    JumpUsedCount = jumpCount,
                    ForwardUsedCount = forwardCount,
                    TurnUsedCount = turnCount,
                    BugUsedCount = bugCount,
                    EfficiencyScore = efficiencyScore,
                    SpeedScore = speedScore,
                    LogicScore = logicScore,
                    CreatedAt = DateTime.Now
                };

                statsList.Add(stat);
                _db.GameStats.Add(stat);
            }

            _db.SaveChanges();

            return BuildResponse(game, statsList);
        }

        // =====================================================================
        // BUILD RESPONSE OBJECT
        // =====================================================================
        private object BuildResponse(Game game, IEnumerable<GameStats> stats)
        {
            var statList = stats.ToList();

            var playerDetails = statList.Select(s =>
            {
                var playerName = _db.Player
                    .AsNoTracking()
                    .Where(p => p.PlayerId == s.PlayerId)
                    .Select(p => p.PlayerName)
                    .FirstOrDefault() ?? $"Player {s.PlayerOrder}";

                string positionLabel = s.FinishPosition switch
                {
                    1 => "🥇 1st",
                    2 => "🥈 2nd",
                    3 => "🥉 3rd",
                    4 => "4th",
                    _ => "—"
                };

                string timeFormatted = s.TimeTakenSeconds.HasValue
                    ? FormatTime(s.TimeTakenSeconds.Value)
                    : "—";

                int overallScore = (s.EfficiencyScore + s.SpeedScore + s.LogicScore) / 3;

                return new
                {
                    playerId = s.PlayerId,
                    playerOrder = s.PlayerOrder,
                    playerName,
                    finishPosition = s.FinishPosition,
                    positionLabel,

                    timeTakenSeconds = s.TimeTakenSeconds,
                    timeFormatted,

                    totalMoves = s.TotalMoves,
                    optimalMoves = s.OptimalMoves,

                    loopUsedCount = s.LoopUsedCount,
                    functionUsedCount = s.FunctionUsedCount,
                    jumpUsedCount = s.JumpUsedCount,
                    forwardUsedCount = s.ForwardUsedCount,
                    turnUsedCount = s.TurnUsedCount,
                    bugUsedCount = s.BugUsedCount,

                    efficiencyScore = s.EfficiencyScore,
                    speedScore = s.SpeedScore,
                    logicScore = s.LogicScore,
                    overallScore
                };
            })
            .OrderBy(p => p.finishPosition ?? 99)
            .ToList();

            return new
            {
                gameId = game.GameId,
                roomCode = game.RoomCode,
                gameStatus = game.GameStatus,
                difficulty = game.DifficultyLevel,
                players = playerDetails
            };
        }

        // =====================================================================
        // SCORE CALCULATIONS
        // =====================================================================

        /// <summary>
        /// Efficiency: how close the player got to the optimal move count.
        /// 100 = perfect (≤ optimal). Drops linearly, floor = 10.
        /// </summary>
        private static int CalculateEfficiencyScore(int totalMoves, int optimalMoves)
        {
            if (totalMoves <= 0) return 0;
            if (totalMoves <= optimalMoves) return 100;

            double ratio = (double)optimalMoves / totalMoves;
            int score = (int)(ratio * 100);
            return Math.Max(score, 10);
        }

        /// <summary>
        /// Speed: based on time taken. Under 2 min = 100. Scales down after.
        /// </summary>
        private static int CalculateSpeedScore(int? timeTakenSeconds)
        {
            if (!timeTakenSeconds.HasValue || timeTakenSeconds.Value <= 0) return 50;

            int t = timeTakenSeconds.Value;
            if (t <= 120) return 100;
            if (t <= 300) return 80;
            if (t <= 600) return 60;
            if (t <= 900) return 40;
            return 20;
        }

        /// <summary>
        /// Logic: rewards use of loops and functions relative to total moves.
        /// More logical usage = higher score.
        /// </summary>
        private static int CalculateLogicScore(int loopCount, int functionCount, int totalMoves)
        {
            if (totalMoves <= 0) return 0;
            double ratio = (double)(loopCount + functionCount * 2) / totalMoves;
            int score = (int)(ratio * 100);
            return Math.Min(score, 100);
        }

        private static string FormatTime(int seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        // =====================================================================
        // TRIGGER STATS COMPUTATION (called internally after game ends)
        // You can call this from GameController.MovePlayer when game completes.
        // Or call it from the GET endpoint lazily.
        // =====================================================================
        [HttpPost("ComputeStats")]
        public IActionResult ComputeStats(int gameId)
        {
            var game = _db.Game.FirstOrDefault(g => g.GameId == gameId);
            if (game == null) return NotFound("Game not found");

            if (game.GameStatus != "Completed")
                return BadRequest("Game not yet completed");

            // Delete old stats if any (re-compute)
            var old = _db.GameStats.Where(s => s.GameId == gameId).ToList();
            if (old.Any())
            {
                _db.GameStats.RemoveRange(old);
                _db.SaveChanges();
            }

            var result = ComputeAndSaveStats(gameId, game);
            if (result == null) return NotFound("No player data found");

            return Ok(result);
        }
    }
}