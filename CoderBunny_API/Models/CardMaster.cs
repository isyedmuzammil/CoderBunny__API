using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class CardMaster
{
    public int CardId { get; set; }

    public string CardName { get; set; } = null!;

    public virtual ICollection<PlayerCardUsage> PlayerCardUsages { get; set; } = new List<PlayerCardUsage>();

    public virtual ICollection<PlayerCard> PlayerCards { get; set; } = new List<PlayerCard>();

    public virtual ICollection<PlayerFunctionCards> PlayerFunctionCards { get; set; } = new List<PlayerFunctionCards>();
}
