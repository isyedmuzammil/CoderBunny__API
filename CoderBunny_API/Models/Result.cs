using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class Result
{
    public int ResultId { get; set; }

    public int? GameId { get; set; }

    public int? PlayerId { get; set; }

    public int? Position { get; set; }

    public string? Remarks { get; set; }

    public virtual Game? Game { get; set; }

    public virtual Player? Player { get; set; }
}
