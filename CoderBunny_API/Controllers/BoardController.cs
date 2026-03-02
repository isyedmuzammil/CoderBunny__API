using CoderBunny_API.Models;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace CoderBunny_API.Controllers
{
    public class BoardController : ApiController
    {
        coderbunnyEntities db = new coderbunnyEntities();

        [HttpGet]
        [Route("api/board/{boardId}")]
        public HttpResponseMessage GetBoardConfig(int boardId)
        {
            var boardElements = db.BoardConfig
                                  .Where(x => x.BoardId == boardId)
                                  .Select(x => new
                                  {
                                      x.BoardId,
                                      x.AssetType,
                                      x.X,
                                      x.Y
                                  })
                                  .ToList();

            if (boardElements == null || boardElements.Count == 0)
            {
                return Request.CreateResponse(HttpStatusCode.NotFound, "Board not found");
            }

            return Request.CreateResponse(HttpStatusCode.OK, boardElements);
        }
        [HttpGet]
        public HttpResponseMessage Ping()
        {
            return Request.CreateResponse(HttpStatusCode.OK, "Akhir kar Chal hi Gaya");

        }
    }
}