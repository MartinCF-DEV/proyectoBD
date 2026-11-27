using Community360.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Community360.Api.Controllers;

/// <summary>
/// Endpoints REST protegidos del Dashboard de Community 360.
/// Todos los endpoints exigen un token JWT válido con rol 'admin' y que no haya sido revocado.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "admin")]
public sealed class DashboardController : ControllerBase
{
    private readonly CloudDatabaseService _dbService;

    public DashboardController(CloudDatabaseService dbService)
    {
        _dbService = dbService;
    }

    [HttpGet("resumen")]
    public async Task<IActionResult> GetResumen()
    {
        var resumen = await _dbService.GetDashboardResumenAsync();
        return Ok(resumen);
    }
}
