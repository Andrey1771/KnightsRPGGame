using KnightsRPGGame.Service.GameAPI.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnightsRPGGame.Service.GameAPI.Controllers
{
    [Route("api/game")]
    public class GameController : Controller
    {
        public IGameRepository _gameRepository;
        public GameController(IGameRepository gameRepository)
        {
            _gameRepository = gameRepository;
        }

        /*
        public IActionResult Index()
        {
            return View();
        }
        */

        [HttpGet]
        //[Authorize]
        public async Task<object> Get()
        {
            //var response = new ResponseDto
            try
            {
                var gameInfoDto = await _gameRepository.GetGameInfo();
                //_response.Result = productDtos;
                return gameInfoDto;
            }
            catch (Exception e)
            {
                // _response.IsSuccess = false;
                //_response.ErrorMessage = new List<string>() { e.ToString() };
                throw e;
            }
        }
    }
}
