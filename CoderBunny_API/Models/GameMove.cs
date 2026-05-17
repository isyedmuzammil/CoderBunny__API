using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class GameMove
{
    public int MoveId { get; set; }

    public int GameId { get; set; }

    public int PlayerId { get; set; }

    public int DiceValue { get; set; }

    public int SequenceId { get; set; }

    public DateTime? MoveTime { get; set; }

    public int? FromX { get; set; }

    public int? FromY { get; set; }

    public int? ToX { get; set; }

    public int? ToY { get; set; }

    public virtual Game Game { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;

    public virtual ICollection<PlayerCardUsage> PlayerCardUsages { get; set; } = new List<PlayerCardUsage>();
}
