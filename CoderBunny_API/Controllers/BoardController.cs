using CoderBunny_API1.Data;
using Microsoft.AspNetCore.Mvc;

namespace CoderBunny_API1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BoardController : ControllerBase
    {
        private readonly AppDbContext db;

        public BoardController(AppDbContext context)
        {
            db = context;
        }

        // =====================================================================
        // GET BOARD CONFIG
        // =====================================================================
        [HttpGet("{boardId}")]
        public IActionResult GetBoardConfig(int boardId)
        {
            var boardElements = db.BoardConfig
                                  .Where(x => x.BoardId == boardId)
                                  .Select(x => new
                                  {
                                      boardId = x.BoardId,
                                      assetType = x.AssetType ?? "",
                                      x = x.X,
                                      y = x.Y
                                  })
                                  .ToList();

            if (boardElements == null || boardElements.Count == 0)
                return NotFound(new
                {
                    message = "Board not found"
                });

            return Ok(boardElements);
        }

        // =====================================================================
        // PING
        // =====================================================================
        [HttpGet("Ping")]
        public IActionResult Ping()
        {
            return Ok(new
            {
                message = "Akhir kar Chal hi Gaya"
            });
        }
    }
}
