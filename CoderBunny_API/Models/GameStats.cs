using CoderBunny_API1.Models;
using System;

namespace CoderBunny_API1_Updated.Models;


public partial class GameStats
{
    public int StatId { get; set; }

    public int GameId { get; set; }

    public int PlayerId { get; set; }

    public int? PlayerOrder { get; set; }

    // Core result
    public int? FinishPosition { get; set; }
    public int? TimeTakenSeconds { get; set; }

    // Move counts
    public int TotalMoves { get; set; } = 0;
    public int OptimalMoves { get; set; } = 0;

    // Card usage breakdown
    public int LoopUsedCount { get; set; } = 0;
    public int FunctionUsedCount { get; set; } = 0;
    public int JumpUsedCount { get; set; } = 0;
    public int ForwardUsedCount { get; set; } = 0;
    public int TurnUsedCount { get; set; } = 0;
    public int BugUsedCount { get; set; } = 0;

    public int EfficiencyScore { get; set; } = 0;
    public int SpeedScore { get; set; } = 0;
    public int LogicScore { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    public virtual Game? Game { get; set; } = null!;
    public virtual Player? Player { get; set; } = null!;
}