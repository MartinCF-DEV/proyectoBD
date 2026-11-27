using Community360.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Community360.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
[Authorize(Roles = "admin")]
public sealed class UsuariosController : ControllerBase
{
    private readonly CloudDatabaseService _dbService;

    public UsuariosController(CloudDatabaseService dbService)
    {
        _dbService = dbService;
    }

    [HttpGet]
    public IActionResult GetUsuarios(
        [FromQuery] string? estatus = null,
        [FromQuery] string? busqueda = null,
        [FromQuery] int limite = 50)
    {
        if (limite <= 0 || limite > 500)
        {
            return BadRequest(new { mensaje = "El parámetro 'limite' debe estar entre 1 y 500." });
        }

        var usuarios = _dbService.GetUsuarios(estatus, busqueda, limite);
        return Ok(new
        {
            total = usuarios.Count,
            datos = usuarios
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUsuarioById(string id)
    {
        var usuario = await _dbService.GetUsuarioByIdAsync(id);
        if (usuario is null)
        {
            return NotFound(new { mensaje = $"No se encontró el usuario '{id}' en Couchbase Capella." });
        }

        var interacciones = _dbService.GetInteracciones(idUsuario: usuario.IdUsuario, limite: 50);
        return Ok(new
        {
            usuario,
            total_interacciones = interacciones.Count,
            interacciones
        });
    }
}
