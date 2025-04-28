using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NotificationsService.Services
{
    [ApiController]
    [Route("api/[controller]")]
    public class PrController : ControllerBase
    {
        private readonly PrService _prService;

        public PrController(PrService prService)
        {
            _prService = prService;
        }

        [HttpGet]
        public IActionResult GetOpenPrs()
        {
            var prs = _prService.GetAllPrs();
            return Ok(prs);
        }
    }
}

