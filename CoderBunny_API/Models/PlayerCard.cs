using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class PlayerCard
{
    public int PlayerCardId { get; set; }

    public int PlayerId { get; set; }

    public int CardId { get; set; }

    public int Quantity { get; set; }

    public int? GameId { get; set; }

    public virtual CardMaster Card { get; set; } = null!;


    public virtual Game? Game { get; set; }

    public virtual Player Player { get; set; } = null!;
}
