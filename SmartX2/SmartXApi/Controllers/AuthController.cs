using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SmartXApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        [HttpGet("me")]
        public ActionResult<object> Me() => Ok(new
        {
            UserId = User.FindFirstValue("user_id") ?? User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = User.FindFirstValue("email") ?? User.FindFirstValue(ClaimTypes.Email),
            Name = User.FindFirstValue("name"),
            Provider = User.FindFirstValue("firebase")
        });
    }
}
