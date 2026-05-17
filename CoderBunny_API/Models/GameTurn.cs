using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class GameTurn
{
    public int GameTurnId { get; set; }

    public int GameId { get; set; }

    public int CurrentPlayerId { get; set; }

    public int TurnNumber { get; set; }

    public virtual Player CurrentPlayer { get; set; } = null!;

    public virtual Game Game { get; set; } = null!;
}
