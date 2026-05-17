using CoderBunny_API1.Models;

public partial class GamePlayers
{
    public int GamePlayerId { get; set; }
    public int GameId { get; set; }
    public int PlayerId { get; set; }
    public int? PlayerOrder { get; set; }
    public int? CurrentPosition { get; set; }
    public bool? IsActive { get; set; }
    public string? Direction { get; set; }
    public bool HasEatenCarrot { get; set; }

    // new feilds
    public bool IsFinished { get; set; } = false;
    public int? FinishPosition { get; set; }
    //updated
    public bool BugUsedInCurrentMove { get; set; } = false;
    public int BugUsesRemaining { get; set; } = 3;
    public virtual Game Game { get; set; } = null!;
    public virtual Player Player { get; set; } = null!;
}