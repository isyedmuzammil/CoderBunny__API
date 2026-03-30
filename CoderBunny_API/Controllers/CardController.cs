using CoderBunny_API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace CoderBunny_API.Controllers
{
    public class CardController : ApiController
    {
        coderbunnyEntities4 db = new coderbunnyEntities4();

        [HttpPost]
        public HttpResponseMessage UseCards(int moveId, [FromUri] List<int> cardIds)
        {
            if (moveId <= 0 || cardIds == null || !cardIds.Any())
                return Request.CreateResponse(HttpStatusCode.BadRequest, "Invalid request data");

            var move = db.GameMove.FirstOrDefault(m => m.MoveId == moveId);
            if (move == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Invalid move");

            // 🔥 Prevent using cards more or less than dice value
            if (cardIds.Count != move.DiceValue)
                return Request.CreateResponse(HttpStatusCode.BadRequest,
                    "Card count must match dice value");

            foreach (var cardId in cardIds)
            {
                var playerCard = db.PlayerCard.FirstOrDefault(pc =>
                    pc.PlayerId == move.PlayerId &&
                    pc.GameId == move.GameId &&
                    pc.CardId == cardId
                );

                if (playerCard == null)
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        $"Invalid CardId {cardId} for this player");

                if (playerCard.Quantity <= 0)
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        $"Not enough quantity for CardId {cardId}");

                // 🔥 Decrease inventory by 1 for EACH card used
                playerCard.Quantity -= 1;

                // 🔥 Insert ONE ROW per card (VERY IMPORTANT)
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

            return Request.CreateResponse(HttpStatusCode.OK, "Cards used successfully");
        }

        //Will check ke player has how many cards left
        [HttpGet]
        public HttpResponseMessage ShowAvailableCards(int playerId, int gameId)
        {
            var cards = db.PlayerCard
                .Where(pc => pc.PlayerId == playerId && pc.GameId == gameId)
                .Select(pc => new
                {
                    pc.CardId,
                    CardName = pc.CardMaster.CardName,//join krke layega Name
                    pc.Quantity
                })
                .ToList();

            if (!cards.Any())
                return Request.CreateResponse(HttpStatusCode.NotFound, "No cards found");

            return Request.CreateResponse(HttpStatusCode.OK, cards);
        }

        //Will tell which cards a player used in his last move
        [HttpGet]
        public HttpResponseMessage GetPlayerCards(int playerId, int gameId, int moveId)
        {
            var usedCards = db.PlayerCardUsage
                .Where(pcu =>
                    pcu.PlayerId == playerId &&
                    pcu.GameId == gameId &&
                    pcu.MoveId == moveId
                )
                .Select(pcu => new
                {
                    pcu.CardId,
                    pcu.QuantityUsed,
                    pcu.UsedAt
                })
                .ToList();

            if (!usedCards.Any())
                return Request.CreateResponse(HttpStatusCode.NotFound, "No card usage found");

            return Request.CreateResponse(HttpStatusCode.OK, usedCards);
        }


        // It will only take move id and will tell which player use which card on which dice no
        [HttpGet]
        public HttpResponseMessage GetMoveCardSummary(int moveId)
        {

            var move = db.GameMove.FirstOrDefault(m => m.MoveId == moveId);

            if (move == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Move not found");

            // Get used cards for this move
            var cardsUsed = db.PlayerCardUsage
                .Where(u => u.MoveId == moveId)
                .Select(u => new
                {
                    u.CardId,
                    u.QuantityUsed
                })
                .ToList();

            if (!cardsUsed.Any())
                return Request.CreateResponse(HttpStatusCode.NotFound, "No cards used in this move");

            // 🔹 Final response
            var result = new
            {
                move.MoveId,
                move.GameId,
                move.PlayerId,
                DiceValue = move.DiceValue,
                CardsUsed = cardsUsed
            };

            return Request.CreateResponse(HttpStatusCode.OK, result);
        }
        [HttpGet]
        public HttpResponseMessage GetPreviousMoves(int playerId, int gameId)
        {
            var moves = db.GameMove
                .Where(m => m.PlayerId == playerId && m.GameId == gameId)
                .OrderByDescending(m => m.MoveId)
                .Select(m => new
                {
                    m.MoveId,
                    DiceValue = m.DiceValue,
                    Cards = db.PlayerCardUsage
                        .Where(c => c.MoveId == m.MoveId)
                        .Select(c => new
                        {
                            c.CardId,
                            CardName = c.CardMaster.CardName   
                        }).ToList()
                })
                .ToList();

            if (!moves.Any())
                return Request.CreateResponse(HttpStatusCode.NotFound, "No moves found");

            return Request.CreateResponse(HttpStatusCode.OK, moves);
        }
        public class FunctionRequest
        {
            public int GameId { get; set; }
            public int PlayerId { get; set; }
            public List<int> CardIds { get; set; }
        }

        [HttpPost]
        public HttpResponseMessage UseFunction([FromBody] FunctionRequest req)
        {
            if (req == null)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, "Request body is null");
            }

            try
            {
                int gameId = req.GameId;
                int playerId = req.PlayerId;
                var cardIds = req.CardIds;

                var existing = db.PlayerFunctionCards
                    .Where(f => f.GameId == gameId && f.PlayerId == playerId)
                    .ToList();

                // 🟢 SAVE FUNCTION
                if (cardIds != null && cardIds.Any())
                {
                    if (existing.Any())
                        return Request.CreateResponse(HttpStatusCode.BadRequest, "Function already saved");

                    int order = 1;

                    foreach (var cardId in cardIds)
                    {
                        db.PlayerFunctionCards.Add(new PlayerFunctionCards
                        {
                            GameId = gameId,
                            PlayerId = playerId,
                            CardId = cardId,
                            OrderNo = order++
                        });
                    }

                    db.SaveChanges();
                    return Request.CreateResponse(HttpStatusCode.OK, "Function saved");
                }

                // 🔵 USE FUNCTION
                if (!existing.Any())
                    return Request.CreateResponse(HttpStatusCode.BadRequest, "No function saved");
                
                var usedCount = db.PlayerCardUsage
                    .Where(u => u.GameId == gameId && u.PlayerId == playerId && u.IsFunction == true)
                    .Select(u => u.MoveId)
                    .Distinct()
                    .Count();

                if (usedCount >= 4)
                    return Request.CreateResponse(HttpStatusCode.BadRequest, "Limit reached");

                var move = db.GameMove
                    .Where(m => m.GameId == gameId && m.PlayerId == playerId)
                    .OrderByDescending(m => m.SequenceId)
                    .FirstOrDefault();
                if (move == null)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest, "No move found. Roll dice first");
                }

                foreach (var card in existing)
                {
                    db.PlayerCardUsage.Add(new PlayerCardUsage
                    {
                        MoveId = move.MoveId,
                        PlayerId = playerId,
                        GameId = gameId,
                        CardId = card.CardId,
                        QuantityUsed = 1,
                        UsedAt = DateTime.Now,
                        IsFunction = true
                    });
                }

                db.SaveChanges();
                return Request.CreateResponse(HttpStatusCode.OK, "Function applied");
            }
            catch (Exception ex)
            {
                return Request.CreateResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        [HttpGet]
        public HttpResponseMessage GetFunction(int gameId, int playerId)
        {
            var cards = db.PlayerFunctionCards
                .Where(f => f.GameId == gameId && f.PlayerId == playerId)
                .OrderBy(f => f.OrderNo)
                .Select(f => f.CardId)
                .ToList();

            return Request.CreateResponse(HttpStatusCode.OK, cards);
        }

    }
}

