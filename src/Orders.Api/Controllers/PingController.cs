using Microsoft.AspNetCore.Mvc;

namespace Orders.Api.Controllers;

[ApiController]
[Route("ping")]
public class PingController : ControllerBase
{
  [HttpGet]
  public IActionResult Get() => Ok(new { status = "ok" });
}