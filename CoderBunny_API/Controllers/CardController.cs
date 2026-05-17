using CoderBunny_API1.Data;
using CoderBunny_API1.Models;
using Microsoft.AspNetCore.Mvc;

namespace CoderBunny_API1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CardController : ControllerBase
    {
        private readonly AppDbContext db;

        public CardController(AppDbContext context)
        {
            db = context;
        }

        // =====================================================================
        // LOOP CARD IDs — PREPROCESSOR CONSTANTS ONLY
        // =====================================================================
        private static readonly HashSet<int> LoopCardIds = new HashSet<int> { 5, 6, 7 };
        private static readonly Dictionary<int, int> LoopCardValues = new Dictionary<int, int>
        {
            { 5, 2 }, // Loop2
            { 6, 3 }, // Loop3
            { 7, 4 }, // Loop4
        };

        // =====================================================================
        // LOOP VALIDATION — PREPROCESSOR ONLY, does NOT touch movement logic
        // Returns null if valid, error message string if invalid
        // =====================================================================
        private string ValidateLoopCards(List<int> cardIds, int diceValue)
        {
            // Check if any loop card exists in the submitted list
            var loopCards = cardIds.Where(id => LoopCardIds.Contains(id)).ToList();

            // No loop card — normal validation, count must equal diceValue
            if (!loopCards.Any())
            {
                if (cardIds.Count != diceValue)
                    return $"Card count must match dice value ({diceValue})";
                return null; // valid
            }

            // RULE: dice = 1 → loop NOT allowed
            if (diceValue == 1)
                return "Loop card cannot be used when dice value is 1";

            // RULE: only one loop card allowed per turn
            if (loopCards.Count > 1)
                return "Only one loop card can be used per turn";

            // RULE: loop card must be FIRST (index 0)
            if (!LoopCardIds.Contains(cardIds[0]))
                return "Loop card must be the first card in selection";

            // RULE: second card (index 1) must be a valid action card (not another loop)
            if (cardIds.Count < 2 || LoopCardIds.Contains(cardIds[1]))
                return "Loop card must be followed by a valid action card";

            // RULE: total submitted cards must equal diceValue
            // (e.g. dice=2 → [Loop2, Forward] = 2 cards submitted)
            // (e.g. dice=3 → [Loop3, Forward, Right] = 3 cards submitted)
            if (cardIds.Count != diceValue)
                return $"Card count must match dice value ({diceValue})";

            // RULE: after expansion, action count must not exceed dice limit
            // Expansion: loopValue replaces the loop+action slot
            // Remaining normal cards = diceValue - 2 (the loop card + the action card it wraps)
            // Total effective actions = loopValue + (diceValue - 2)
            // We only need to ensure submitted count == diceValue (already checked above)

            return null; // valid
        }

        // =====================================================================
        // USE CARDS
        // =====================================================================
        public class UseCardsRequest
        {
            public int MoveId { get; set; }
            public List<int> CardIds { get; set; }
        }

        [HttpPost("UseCards")]
        public IActionResult UseCards([FromBody] UseCardsRequest request)
        {
            if (request == null || request.MoveId <= 0 || request.CardIds == null || !request.CardIds.Any())
                return BadRequest("Invalid request data");

            var move = db.GameMove.FirstOrDefault(m => m.MoveId == request.MoveId);
            if (move == null)
                return NotFound("Invalid move");

            var game = db.Game.FirstOrDefault(g => g.GameId == move.GameId);
            if (game == null)
                return NotFound("Game not found");

            if (game.GameStatus != "Running")
                return BadRequest("Game is not running");

            var currentTurn = db.GameTurn
                .Where(t => t.GameId == move.GameId)
                .OrderBy(t => t.TurnNumber)
                .FirstOrDefault();

            if (currentTurn == null || currentTurn.CurrentPlayerId != move.PlayerId)
                return BadRequest("Not your turn");

            // =====================================================================
            // LOOP PREPROCESSOR — validate loop rules BEFORE existing card checks
            // This only validates; movement expansion happens in GameController.MovePlayer
            // =====================================================================
            var loopValidationError = ValidateLoopCards(request.CardIds, move.DiceValue);
            if (loopValidationError != null)
                return BadRequest(loopValidationError);
            // =====================================================================

            foreach (var cardId in request.CardIds)
            {
                var playerCard = db.PlayerCard.FirstOrDefault(pc =>
                    pc.PlayerId == move.PlayerId &&
                    pc.GameId == move.GameId &&
                    pc.CardId == cardId);

                if (playerCard == null)
                    return BadRequest($"Invalid CardId {cardId}");

                if (playerCard.Quantity <= 0)
                    return BadRequest($"Not enough quantity for CardId {cardId}");

                playerCard.Quantity -= 1;

                db.PlayerCardUsage.Add(new PlayerCardUsage
                {
                    MoveId = move.MoveId,
                    PlayerId = move.PlayerId,
                    GameId = move.GameId,
                    CardId = cardId,
                    QuantityUsed = 1,
                    UsedAt = DateTime.Now
                });
            }

            db.SaveChanges();
            return Ok(new { message = "Cards used successfully" });
        }

        // =====================================================================
        // SHOW AVAILABLE CARDS (FIXED - NO CardMaster)
        // =====================================================================
        [HttpGet("ShowAvailableCards")]
        public IActionResult ShowAvailableCards(int playerId, int gameId)
        {
            var cards = (from pc in db.PlayerCard
                         join c in db.CardMaster on pc.CardId equals c.CardId
                         where pc.PlayerId == playerId && pc.GameId == gameId
                         select new
                         {
                             pc.CardId,
                             CardName = c.CardName,
                             pc.Quantity
                         }).ToList();

            if (!cards.Any())
                return NotFound("No cards found");

            return Ok(cards);
        }

        // =====================================================================
        // GET PLAYER CARDS
        // =====================================================================
        [HttpGet("GetPlayerCards")]
        public IActionResult GetPlayerCards(int playerId, int gameId, int moveId)
        {
            var usedCards = db.PlayerCardUsage
                .Where(pcu => pcu.PlayerId == playerId &&
                              pcu.GameId == gameId &&
                              pcu.MoveId == moveId)
                .Select(pcu => new
                {
                    pcu.CardId,
                    pcu.QuantityUsed,
                    pcu.UsedAt
                })
                .ToList();

            if (!usedCards.Any())
                return NotFound("No card usage found");

            return Ok(usedCards);
        }

        // =====================================================================
        // MOVE CARD SUMMARY
        // =====================================================================
        [HttpGet("GetMoveCardSummary")]
        public IActionResult GetMoveCardSummary(int moveId)
        {
            var move = db.GameMove.FirstOrDefault(m => m.MoveId == moveId);

            if (move == null)
                return NotFound("Move not found");

            var cardsUsed = db.PlayerCardUsage
                .Where(u => u.MoveId == moveId)
                .Select(u => new
                {
                    u.CardId,
                    u.QuantityUsed
                })
                .ToList();

            if (!cardsUsed.Any())
                return NotFound("No cards used in this move");

            return Ok(new
            {
                move.MoveId,
                move.GameId,
                move.PlayerId,
                move.DiceValue,
                CardsUsed = cardsUsed
            });
        }

        // =====================================================================
        // GET PREVIOUS MOVES (FIXED)
        // =====================================================================
        [HttpGet("GetPreviousMoves")]
        public IActionResult GetPreviousMoves(int playerId, int gameId)
        {
            var moves = db.GameMove
                .Where(m => m.PlayerId == playerId && m.GameId == gameId)
                .OrderByDescending(m => m.MoveId)
                .ToList()
                .Select(m => new
                {
                    m.MoveId,
                    m.DiceValue,
                    Cards = (from c in db.PlayerCardUsage
                             join cm in db.CardMaster on c.CardId equals cm.CardId
                             where c.MoveId == m.MoveId
                             select new
                             {
                                 c.CardId,
                                 CardName = cm.CardName
                             }).ToList()
                })
                .ToList();

            if (!moves.Any())
                return NotFound("No moves found");

            return Ok(moves);
        }

        // =====================================================================
        // FUNCTION MODEL
        // =====================================================================
        public class FunctionRequest
        {
            public int GameId { get; set; }
            public int PlayerId { get; set; }
            public List<int>? CardIds { get; set; }
        }

        // =====================================================================
        // USE FUNCTION
        // =====================================================================
        [HttpPost("UseFunction")]
        public IActionResult UseFunction([FromBody] FunctionRequest req)
        {
            if (req == null)
                return BadRequest("Request is null");

            var existing = db.PlayerFunctionCards
                .Where(f => f.GameId == req.GameId && f.PlayerId == req.PlayerId)
                .ToList();

            if (req.CardIds != null && req.CardIds.Any())
            {
                if (existing.Any())
                    return BadRequest("Function already saved");

                int order = 1;

                foreach (var cardId in req.CardIds)
                {
                    db.PlayerFunctionCards.Add(new PlayerFunctionCards
                    {
                        GameId = req.GameId,
                        PlayerId = req.PlayerId,
                        CardId = cardId,
                        OrderNo = order++
                    });
                }

                db.SaveChanges();
                return Ok(new { message = "Function saved" });
            }

            if (!existing.Any())
                return BadRequest("No function saved");

            var move = db.GameMove
                .Where(m => m.GameId == req.GameId && m.PlayerId == req.PlayerId)
                .OrderByDescending(m => m.SequenceId)
                .FirstOrDefault();

            if (move == null)
                return BadRequest("No move found");

            foreach (var card in existing)
            {
                db.PlayerCardUsage.Add(new PlayerCardUsage
                {
                    MoveId = move.MoveId,
                    PlayerId = req.PlayerId,
                    GameId = req.GameId,
                    CardId = card.CardId,
                    QuantityUsed = 1,
                    UsedAt = DateTime.Now,
                    IsFunction = true
                });
            }

            db.SaveChanges();
            return Ok(new { message = "Function applied" });
        }

        // =====================================================================
        // GET FUNCTION
        // =====================================================================
        [HttpGet("GetFunction")]
        public IActionResult GetFunction(int gameId, int playerId)
        {
            var cards = db.PlayerFunctionCards
                .Where(f => f.GameId == gameId && f.PlayerId == playerId)
                .OrderBy(f => f.OrderNo)
                .Select(f => f.CardId)
                .ToList();

            return Ok(cards);
        }
    }
}
