using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class Player
{
    public int PlayerId { get; set; }

    public string? PlayerName { get; set; }

    public string? PlayerImage { get; set; }

    public int? GameId { get; set; }

    public virtual Game? Game { get; set; }

    public virtual ICollection<GameMove> GameMoves { get; set; } = new List<GameMove>();

    public virtual ICollection<GamePlayers> GamePlayers { get; set; } = new List<GamePlayers>();

    public virtual ICollection<GameTurn> GameTurns { get; set; } = new List<GameTurn>();

    public virtual ICollection<PlayerCardUsage> PlayerCardUsages { get; set; } = new List<PlayerCardUsage>();

    public virtual ICollection<PlayerCard> PlayerCards { get; set; } = new List<PlayerCard>();

    public virtual ICollection<PlayerFunctionCards> PlayerFunctionCards { get; set; } = new List<PlayerFunctionCards>();

    public virtual ICollection<Result> Results { get; set; } = new List<Result>();
}
