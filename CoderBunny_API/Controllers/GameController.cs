using CoderBunny_API.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace CoderBunny_API.Controllers
{
    public class GameController : ApiController
    {
        coderbunnyEntities db = new coderbunnyEntities();

        //To start a Game
        [HttpPost]
        public HttpResponseMessage StartGame(string difficulty, string gameStatus, [FromUri] int[] playerIds)
        {
            try
            {
                // Not Allowing duplicate ID
                if (playerIds.Length != playerIds.Distinct().Count())
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest,"Player ID duplication conflict"
                    );
                }

                // 1️ Game add
                Game g = new Game()
                {
                    DifficultyLevel = difficulty,
                    NumberOfPlayers = playerIds.Length,
                    GameStatus = gameStatus,
                    RoomCode = GenerateRoomCode()

                };
                db.Game.Add(g);
                db.SaveChanges();

                //CardMaster sa Sab Cards Fetch karengy hum
                var allCards = db.CardMaster.Select(c => c.CardId).ToList();


                // 2️ Players add
                int order = 1;
                foreach (int pid in playerIds)
                {
                    db.GamePlayers.Add(new GamePlayers()
                    {
                        GameId = g.GameId,
                        PlayerId = pid,
                        PlayerOrder = order,
                        CurrentPosition = 72,
                        Direction = "right",
                        IsActive = true
                    });

                    db.GameTurn.Add(new GameTurn()
                    {
                        GameId = g.GameId,
                        CurrentPlayerId = pid,
                        TurnNumber = order
                    });

                    // Assign ALL cards to player with Quantity = 10
                    foreach (int cardId in allCards)
                    {
                        db.PlayerCard.Add(new PlayerCard()
                        {
                            PlayerId = pid,
                            GameId = g.GameId,
                            CardId = cardId,
                            Quantity = 10
                        });
                    }
                    order++; 
                }

                db.SaveChanges();

                return Request.CreateResponse(HttpStatusCode.OK, new
                {
                    message = "Game Started Successfully",
                    gameId = g.GameId
                });
            }
            catch (Exception ex)
            {
                return Request.CreateResponse(
                    HttpStatusCode.InternalServerError,
                    ex.InnerException?.InnerException?.Message ?? ex.Message
                );
            }

        }
        private string GenerateRoomCode()
        {
            var random = new Random();
            string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            char first = letters[random.Next(letters.Length)];
            char second = letters[random.Next(letters.Length)];
            int numbers = random.Next(1000, 9999);

            return $"{first}{second}{numbers}";
        }
        //Resume Game
        [HttpPost]
        public HttpResponseMessage ResumeGame(int gameId)
        {
            var game = db.Game.FirstOrDefault(x => x.GameId == gameId);

            if (game == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Game Not Found");

            if (game.GameStatus == "Completed")
                return Request.CreateResponse(HttpStatusCode.BadRequest, "Game already ended");

            game.GameStatus = "Running";
            db.SaveChanges();

            return Request.CreateResponse(HttpStatusCode.OK, "Running");
        }

        //Game Pause

        [HttpPost]
        public HttpResponseMessage PauseGame(int gameId)
        {
            var game = db.Game.FirstOrDefault(x => x.GameId == gameId);

            if (game == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Game Not Found");

            if (game.GameStatus == "Completed")
                return Request.CreateResponse(HttpStatusCode.BadRequest, "Game already ended");

            game.GameStatus = "GamePaused";
            db.SaveChanges();

            return Request.CreateResponse(HttpStatusCode.OK, "Game Paused");
        }

        //To End Game
        [HttpPost]
        public HttpResponseMessage EndGame(int gameId)
        {
            var game = db.Game.FirstOrDefault(x => x.GameId == gameId);

            if (game == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Game Not Found");

            game.GameStatus = "Completed";
            db.SaveChanges();

            return Request.CreateResponse(HttpStatusCode.OK, "Game Ended Successfully");
        }

        //To see Game State
        [HttpGet]
        public HttpResponseMessage GetGameState(int gameId)
        {
            var game = db.Game.Where(x => x.GameId == gameId).Select(x => x.GameStatus).FirstOrDefault();
            if (game == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Game Not Found");

            return Request.CreateResponse(HttpStatusCode.OK, game);
        }

        //Roll dice
        [HttpPost]
        public HttpResponseMessage RollDice([FromBody] GameMoveRequest request)
        {
            if (request == null)
                return Request.CreateResponse(HttpStatusCode.BadRequest, "Invalid request data.");

            var gameExists = db.Game.Any(g => g.GameId == request.GameId);
          
            if (!gameExists)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Game not found.");

            // we will Check if player belongs to this game or not
            var isPlayerInGame = db.GamePlayers.Any(gp =>
                gp.GameId == request.GameId &&
                gp.PlayerId == request.PlayerId &&
                gp.IsActive == true
            );

            if (!isPlayerInGame)
                return Request.CreateResponse(HttpStatusCode.BadRequest,"Player does not belong to this game");

            Random rnd = new Random();
            int diceValue = rnd.Next(1, 4);

            var gameMove = new GameMove
            {
                GameId = request.GameId,
                PlayerId = request.PlayerId,
                DiceValue = diceValue,
                SequenceId = GetNextSequenceId(request.GameId, request.PlayerId),
                MoveTime = DateTime.Now
            };

            db.GameMove.Add(gameMove);
            db.SaveChanges();

            return Request.CreateResponse(HttpStatusCode.OK, new
            {
                gameMove.MoveId,
                gameMove.DiceValue
            });
        }

        [HttpPost]
        public HttpResponseMessage MovePlayer(int moveId)
        {
            var move = db.GameMove.FirstOrDefault(m => m.MoveId == moveId);
            if (move == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Move not found");

            var player = db.GamePlayers.FirstOrDefault(p =>
                p.GameId == move.GameId &&
                p.PlayerId == move.PlayerId &&
                p.IsActive == true
            );

            if (player == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Player not found");

            // ✅ Ensure starting values only once
            if (player.CurrentPosition == null)
                player.CurrentPosition = 72;

            if (string.IsNullOrEmpty(player.Direction))
                player.Direction = "right";

            int oldPosition = player.CurrentPosition.Value;
            string direction = player.Direction;

            var usedCards = db.PlayerCardUsage
                .Where(u => u.MoveId == moveId)
                .ToList();

            int currentPosition = oldPosition;

            foreach (var card in usedCards)
            {
                switch (card.CardId)
                {
                    case 3: // Forward
                        currentPosition = MoveOneStep(currentPosition, direction);
                        break;

                    case 1: // Jump
                        currentPosition = MoveOneStep(currentPosition, direction);
                        currentPosition = MoveOneStep(currentPosition, direction);
                        break;

                    case 2: // Right
                        direction = TurnRight(direction);
                        break;

                    case 4: // Left
                        direction = TurnLeft(direction);
                        break;
                }
            }

            if (currentPosition < 0) currentPosition = 0;
            if (currentPosition > 80) currentPosition = 80;

            player.CurrentPosition = currentPosition;
            player.Direction = direction;

            move.FromX = oldPosition;
            move.ToX = currentPosition;

            db.SaveChanges();

            return Request.CreateResponse(HttpStatusCode.OK, new
            {
                move.MoveId,
                move.GameId,
                move.PlayerId,
                OldPosition = oldPosition,
                NewPosition = currentPosition,
                Direction = direction
            });
        }
        private int MoveOneStep(int pos, string direction)
        {
            if (direction == "up") return pos - 9;
            if (direction == "down") return pos + 9;
            if (direction == "left") return pos - 1;
            if (direction == "right") return pos + 1;
            return pos;
        }

        private string TurnRight(string direction)
        {
            if (direction == "up") return "right";
            if (direction == "right") return "down";
            if (direction == "down") return "left";
            if (direction == "left") return "up";
            return direction;
        }

        private string TurnLeft(string direction)
        {
            if (direction == "up") return "left";
            if (direction == "left") return "down";
            if (direction == "down") return "right";
            if (direction == "right") return "up";
            return direction;
        }


        //to see player current location
        [HttpGet]
        public HttpResponseMessage GetPlayerPositions(int gameId)
        {
            var players = db.GamePlayers
                .Where(p => p.GameId == gameId && p.IsActive == true)
                .Select(p => new
                {
                    p.PlayerId,
                    p.CurrentPosition,
                    p.Direction
                })
                .ToList();

            return Request.CreateResponse(HttpStatusCode.OK, players);
        }
        // Request model
        public class GameMoveRequest
        {
            public int GameId { get; set; }
            public int PlayerId { get; set; }
        }

        // helper for sequence
        private int GetNextSequenceId(int gameId, int playerId)
        {
            var lastMove = db.GameMove
                .Where(m => m.GameId == gameId && m.PlayerId == playerId)
                .OrderByDescending(m => m.SequenceId)
                .FirstOrDefault();

            return (lastMove?.SequenceId ?? 0) + 1;
        }


        [HttpGet]
        public HttpResponseMessage GetDiceByPlayer(int playerId , int gameId)
        {
            var list = db.GameMove
                         .Where(x => x.PlayerId == playerId)
                         .Where(x => x.GameId == gameId)
                         .OrderBy(x => x.SequenceId)
                         .Select(x => new
                         {
                             //x.MoveId,
                             x.GameId,
                             //GameStatus = x.Game.GameStatus,
                             x.SequenceId,
                             x.DiceValue
                             
                             
                         })
                         .ToList();

            return Request.CreateResponse(HttpStatusCode.OK, list);
        }

        [HttpPost]
        public HttpResponseMessage Ping([FromBody]String name)
        {
            return Request.CreateResponse(HttpStatusCode.OK, name);

        }

    }

}