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

    if (request.CardIds.Count != move.DiceValue)
        return BadRequest("Card count must match dice value");

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
                return Ok("Function saved");
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
            return Ok("Function applied");
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
