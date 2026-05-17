using CoderBunny_API1_Updated.Models;
using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class Game
{
    public int GameId { get; set; }

    public string? DifficultyLevel { get; set; }

    public int? NumberOfPlayers { get; set; }

    public string? GameStatus { get; set; }

    public string? RoomCode { get; set; }

    public bool IsTossPhase { get; set; } = false;

    public int TossRound { get; set; } = 1;

    public string? Destination { get; set; }

    // ✅ NEW: for stats timing calculation
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public virtual ICollection<GameMove> GameMoves { get; set; } = new List<GameMove>();
    public virtual ICollection<GamePlayers> GamePlayers { get; set; } = new List<GamePlayers>();
    public virtual ICollection<GameTurn> GameTurns { get; set; } = new List<GameTurn>();
    public virtual ICollection<PlayerCardUsage> PlayerCardUsages { get; set; } = new List<PlayerCardUsage>();
    public virtual ICollection<PlayerCard> PlayerCards { get; set; } = new List<PlayerCard>();
    public virtual ICollection<PlayerFunctionCards> PlayerFunctionCards { get; set; } = new List<PlayerFunctionCards>();
    public virtual ICollection<Player> Players { get; set; } = new List<Player>();
    public virtual ICollection<Result> Results { get; set; } = new List<Result>();

    // ✅ NEW
    public virtual ICollection<GameStats> GameStats { get; set; } = new List<GameStats>();
}
