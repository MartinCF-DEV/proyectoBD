using System.Text.Json;
using Community360.Api.Models;
using Community360.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Community360.Api.Controllers;

[ApiController]
[Route("api/interacciones")]
[Authorize(Roles = "admin")]
public sealed class InteraccionesController : ControllerBase
{
    private readonly CloudDatabaseService _dbService;
    private readonly WebSocketHubService _wsHub;

    public InteraccionesController(CloudDatabaseService dbService, WebSocketHubService wsHub)
    {
        _dbService = dbService;
        _wsHub = wsHub;
    }

    [HttpGet]
    public IActionResult GetInteracciones(
        [FromQuery(Name = "id_usuario")] string? idUsuario = null,
        [FromQuery] string? tema = null,
        [FromQuery] string? canal = null,
        [FromQuery] string? estado = null,
        [FromQuery] string? etiqueta = null,
        [FromQuery] int limite = 50)
    {
        if (limite <= 0 || limite > 500)
        {
            return BadRequest(new { mensaje = "El parámetro 'limite' debe estar entre 1 y 500." });
        }

        var items = _dbService.GetInteracciones(idUsuario, tema, canal, estado, etiqueta, limite);
        return Ok(new
        {
            total = items.Count,
            datos = items
        });
    }

    [HttpGet("{id}")]
    public IActionResult GetInteraccionById(string id)
    {
        var item = _dbService.GetInteraccionById(id);
        if (item is null)
        {
            return NotFound(new { mensaje = $"No se encontró la interacción '{id}'." });
        }
        return Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> CrearInteraccion([FromBody] CrearInteraccionRequest request)
    {
        var errores = ValidadorInteracciones.ValidarDatos(
            request.Canal,
            request.Tipo,
            request.Tema,
            request.Texto,
            request.Sentimiento,
            request.Dispositivo,
            request.ReaccionesRecibidas,
            request.Etiquetas);

        if (errores.Count > 0)
        {
            return BadRequest(new
            {
                mensaje = "Datos de interacción inválidos.",
                errores
            });
        }

        var resumenAntes = await _dbService.GetDashboardResumenAsync();
        var nAntes = resumenAntes.TotalInteraccionesValidas;

        var (creada, statusCode, errorMsg) = await _dbService.CreateInteraccionAsync(request);
        if (creada is null)
        {
            return StatusCode(statusCode, new { mensaje = errorMsg });
        }

        var resumenDespues = await _dbService.GetDashboardResumenAsync();
        await _wsHub.BroadcastEventoAsync(new
        {
            metodo = "POST",
            codigo = 201,
            resumen = $"{creada.IdInteraccion} · {creada.Tema} · {creada.Tipo}",
            hora = DateTime.Now.ToString("HH:mm:ss"),
            peticion = $"POST /api/interacciones\nAuthorization: Bearer ••••••\n\n{JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true })}",
            respuesta = $"201 Created\n{JsonSerializer.Serialize(creada, new JsonSerializerOptions { WriteIndented = true })}",
            efecto = $"Cambio en el dashboard → N: {nAntes} → {resumenDespues.TotalInteraccionesValidas} · conteo({creada.Tema}) +1 · {creada.Tipo} +1",
            dashboard = resumenDespues
        });

        return CreatedAtAction(
            nameof(GetInteraccionById),
            new { id = creada.IdInteraccion },
            creada);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> ActualizarInteraccion(string id, [FromBody] ActualizarInteraccionRequest request)
    {
        var errores = ValidadorInteracciones.ValidarDatos(
            request.Canal,
            request.Tipo,
            request.Tema,
            request.Texto,
            request.Sentimiento,
            request.Dispositivo,
            request.ReaccionesRecibidas,
            request.Etiquetas);

        if (errores.Count > 0)
        {
            return BadRequest(new
            {
                mensaje = "Datos de actualización inválidos.",
                errores
            });
        }

        var previa = _dbService.GetInteraccionById(id);
        var (actualizada, statusCode, errorMsg) = await _dbService.UpdateInteraccionAsync(id, request);
        if (actualizada is null)
        {
            return StatusCode(statusCode, new { mensaje = errorMsg });
        }

        var resumenDespues = await _dbService.GetDashboardResumenAsync();
        await _wsHub.BroadcastEventoAsync(new
        {
            metodo = "PUT",
            codigo = 200,
            resumen = $"{actualizada.IdInteraccion} · {previa?.Tema ?? actualizada.Tema} → {actualizada.Tema}",
            hora = DateTime.Now.ToString("HH:mm:ss"),
            peticion = $"PUT /api/interacciones/{actualizada.IdInteraccion}\nAuthorization: Bearer ••••••\n\n{JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true })}",
            respuesta = $"200 OK\n{JsonSerializer.Serialize(actualizada, new JsonSerializerOptions { WriteIndented = true })}",
            efecto = $"Cambio en el dashboard → N sin cambio ({resumenDespues.TotalInteraccionesValidas}) · {actualizada.IdInteraccion} actualizado ({actualizada.Tema}, {actualizada.Canal})",
            dashboard = resumenDespues
        });

        return Ok(actualizada);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> EliminarInteraccion(string id)
    {
        var previa = _dbService.GetInteraccionById(id);
        var resumenAntes = await _dbService.GetDashboardResumenAsync();
        var nAntes = resumenAntes.TotalInteraccionesValidas;

        var (eliminada, statusCode, errorMsg) = await _dbService.DeleteInteraccionAsync(id);
        if (!eliminada)
        {
            return StatusCode(statusCode, new { mensaje = errorMsg });
        }

        var normId = id.Trim().ToUpperInvariant();
        var respObj = new
        {
            id_interaccion = normId,
            mensaje = "Interacción eliminada de las 5 tablas de Cassandra."
        };

        var resumenDespues = await _dbService.GetDashboardResumenAsync();
        await _wsHub.BroadcastEventoAsync(new
        {
            metodo = "DELETE",
            codigo = 200,
            resumen = $"{normId} · {previa?.Tema ?? "eliminada"}",
            hora = DateTime.Now.ToString("HH:mm:ss"),
            peticion = $"DELETE /api/interacciones/{normId}\nAuthorization: Bearer ••••••",
            respuesta = $"200 OK\n{JsonSerializer.Serialize(respObj, new JsonSerializerOptions { WriteIndented = true })}",
            efecto = $"Cambio en el dashboard → N: {nAntes} → {resumenDespues.TotalInteraccionesValidas} · conteo({previa?.Tema ?? "tema"}) −1",
            dashboard = resumenDespues
        });

        return Ok(respObj);
    }
}
