using System;
using System.Collections.Generic;

namespace CoderBunny_API1.Models;

public partial class BoardConfig
{
    public int Id { get; set; }

    public int BoardId { get; set; }

    public string? AssetType { get; set; }

    public int X { get; set; }

    public int Y { get; set; }
}
