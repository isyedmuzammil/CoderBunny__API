using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class PlayerFunctionCards
{
    public int Id { get; set; }

    public int GameId { get; set; }

    public int PlayerId { get; set; }

    public int CardId { get; set; }

    public int OrderNo { get; set; }

    public int? UsageCount { get; set; }

    public int? MaxUsage { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public virtual CardMaster Card { get; set; } = null!;

    public virtual Game Game { get; set; } = null!;

    public virtual Player Player { get; set; } = null!;
}
