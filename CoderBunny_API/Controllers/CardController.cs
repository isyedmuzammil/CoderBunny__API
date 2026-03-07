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
        coderbunnyEntities db = new coderbunnyEntities();

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
                            c.CardId
                        }).ToList()
                })
                .ToList();

            if (!moves.Any())
                return Request.CreateResponse(HttpStatusCode.NotFound, "No moves found");

            return Request.CreateResponse(HttpStatusCode.OK, moves);
        }

    }
}

