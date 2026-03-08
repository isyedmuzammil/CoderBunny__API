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
                        CurrentPosition = 64,
                        Direction = "up",
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
        //to resume the old game 
        [HttpGet]
        public HttpResponseMessage GetPausedGames()
        {
            var games = db.Game
                .Where(g => g.GameStatus == "GamePaused")
                .Join(db.GamePlayers,
                    g => g.GameId,
                    gp => gp.GameId,
                    (g, gp) => new
                    {
                        gameId = g.GameId,
                        roomCode = g.RoomCode,
                        playerId = gp.PlayerId
                    })
                .ToList();

            return Request.CreateResponse(HttpStatusCode.OK, games);
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
        [HttpPost]
        public HttpResponseMessage RestartGame(int gameId)
        {
            try
            {
                var game = db.Game.FirstOrDefault(g => g.GameId == gameId);

                if (game == null)
                    return Request.CreateResponse(HttpStatusCode.NotFound, "Game not found");

                // 1️⃣ Delete all moves
                var moves = db.GameMove.Where(m => m.GameId == gameId).ToList();
                var moveIds = moves.Select(m => m.MoveId).ToList();

                // 2️⃣ Delete card usages related to moves
                var cardUsage = db.PlayerCardUsage
                    .Where(c => moveIds.Contains(c.MoveId))
                    .ToList();

                db.PlayerCardUsage.RemoveRange(cardUsage);
                db.GameMove.RemoveRange(moves);

                // 3️⃣ Reset player positions
                var players = db.GamePlayers.Where(p => p.GameId == gameId).ToList();

                foreach (var player in players)
                {
                    player.CurrentPosition = 64;
                    player.Direction = "up";
                    player.HasEatenCarrot = false;
                }

                // 4️⃣ Reset player cards
                var playerCards = db.PlayerCard.Where(pc => pc.GameId == gameId).ToList();

                foreach (var card in playerCards)
                {
                    card.Quantity = 10;
                }

                // 5️⃣ Reset game status
                game.GameStatus = "Running";

                db.SaveChanges();

                return Request.CreateResponse(HttpStatusCode.OK, new
                {
                    message = "Game restarted successfully",
                    gameId = gameId
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

            // we need to make sure player use card before rolling dice again
            var lastMove = db.GameMove
                .Where(m => m.GameId == request.GameId &&
                            m.PlayerId == request.PlayerId)
                .OrderByDescending(m => m.SequenceId)
                .FirstOrDefault();

            if (lastMove != null)
            {
                bool cardsUsed = db.PlayerCardUsage
                    .Any(u => u.MoveId == lastMove.MoveId);

                if (!cardsUsed)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        "You must use cards before rolling dice again");
                }
            }

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

            var game = db.Game.FirstOrDefault(g => g.GameId == move.GameId);
            if (game == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Game not found");

            if (player.CurrentPosition == null)
                player.CurrentPosition = 64;

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
                    case 3:
                        currentPosition = MoveOneStep(currentPosition, direction);
                        // 🥕 BLUE CARROT AT INDEX 20
                        if (currentPosition == 20)
                        {
                            player.HasEatenCarrot = true;
                        }
                        break;

                    case 1:
                        currentPosition = MoveOneStep(currentPosition, direction);
                        // 🥕 BLUE CARROT AT INDEX 20
                        if (currentPosition == 20)
                        {
                            player.HasEatenCarrot = true;
                        }
                        currentPosition = MoveOneStep(currentPosition, direction);
                        // 🥕 BLUE CARROT AT INDEX 20
                        if (currentPosition == 20)
                        {
                            player.HasEatenCarrot = true;
                        }
                        break;

                    case 2:
                        direction = TurnRight(direction);
                        continue;

                    case 4:
                        direction = TurnLeft(direction);
                        continue;
                }

                if (currentPosition < 0 || currentPosition > 80)
                    return Request.CreateResponse(HttpStatusCode.BadRequest, "Out of board");

                // 🔥 Convert index to X,Y
                int x = currentPosition % 9;
                int y = currentPosition / 9;

                // 🚧 Check hurdle or carrot
                var boardItem = db.BoardConfig.FirstOrDefault(b =>
                    b.BoardId == move.GameId &&   // assuming BoardId == GameId
                    b.X == x &&
                    b.Y == y
                );

                if (boardItem != null)
                {
                    if (boardItem.AssetType == "Puddle" ||
                        boardItem.AssetType == "Fence")
                    {
                        return Request.CreateResponse(HttpStatusCode.OK, new
                        {
                            move.MoveId,
                            OldPosition = oldPosition,
                            NewPosition = oldPosition,
                            Direction = direction,
                            Message = "Blocked"
                        });
                    }

                }

                // 👥 Check other players
                bool playerExists = db.GamePlayers.Any(p =>
                    p.GameId == move.GameId &&
                    p.PlayerId != player.PlayerId &&
                    p.CurrentPosition == currentPosition &&
                    p.IsActive == true
                );

                if (playerExists)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        "Tile already occupied");
                }
            }

            if (currentPosition == 40)
            {
                if (!player.HasEatenCarrot)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        "Eat your carrot before reaching destination");
                }

                // 🏆 Count total moves for this player in this game
                int totalMoves = db.GameMove.Count(m =>
                    m.GameId == move.GameId &&
                    m.PlayerId == move.PlayerId
                );

                db.Result.Add(new Result
                {
                    GameId = move.GameId,
                    PlayerId = player.PlayerId,
                    Position = 1,
                    Remarks = "Winner"
                });

                game.GameStatus = "Completed";

                db.SaveChanges();

                return Request.CreateResponse(HttpStatusCode.OK, new
                {
                    GameStatus = "Completed",
                    TotalMoves = totalMoves
                });
            }

            player.CurrentPosition = currentPosition;
            player.Direction = direction;

            move.FromX = oldPosition;
            move.ToX = currentPosition;

            db.SaveChanges();

            return Request.CreateResponse(HttpStatusCode.OK, new
            {
                move.MoveId,
                OldPosition = oldPosition,
                NewPosition = currentPosition,
                Direction = direction,
                HasEatenCarrot = player.HasEatenCarrot,
                GameStatus = game.GameStatus


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


        // to go to previous position means undo
        [HttpPost]
        public HttpResponseMessage UndoLastMove(int gameId, int playerId)
        {
            var lastMove = db.GameMove
                .Where(m => m.GameId == gameId && m.PlayerId == playerId)
                .OrderByDescending(m => m.SequenceId)
                .FirstOrDefault();

            if (lastMove == null)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, "No move to undo");
            }

            var player = db.GamePlayers.FirstOrDefault(p =>
                p.GameId == gameId && p.PlayerId == playerId);

            if (player == null)
                return Request.CreateResponse(HttpStatusCode.NotFound, "Player not found");

            // move back to previous position
            int previousPosition = lastMove.FromX ?? player.CurrentPosition.Value;

            player.CurrentPosition = previousPosition;

            // delete related card usages
            var usages = db.PlayerCardUsage
                           .Where(u => u.MoveId == lastMove.MoveId)
                           .ToList();

            db.PlayerCardUsage.RemoveRange(usages);

            // delete the move
            db.GameMove.Remove(lastMove);

            db.SaveChanges();

            return Request.CreateResponse(HttpStatusCode.OK, new
            {
                message = "Move undone",
                NewPosition = previousPosition
            });
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
