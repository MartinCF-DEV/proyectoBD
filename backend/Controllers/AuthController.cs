using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Community360.Api.Models;
using Community360.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Community360.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly CloudDatabaseService _dbService;
    private readonly JwtTokenService _jwtService;
    private readonly TokenRevocationService _revocationService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        CloudDatabaseService dbService,
        JwtTokenService jwtService,
        TokenRevocationService revocationService,
        ILogger<AuthController> logger)
    {
        _dbService = dbService;
        _jwtService = jwtService;
        _revocationService = revocationService;
        _logger = logger;
    }

    /// <summary>
    /// Valida las credenciales del administrador contra la colección 'cuentas' en Couchbase Capella,
    /// verificando el hash seguro BCrypt y emitiendo un token JWT firmado.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                mensaje = "Datos de inicio de sesión inválidos.",
                errores = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
            });
        }

        var correo = request.Correo.Trim().ToLowerInvariant();
        var cuenta = await _dbService.GetCuentaByCorreoAsync(correo);

        if (cuenta is null)
        {
            _logger.LogWarning("Intento de inicio de sesión fallido: cuenta '{Correo}' no encontrada en Couchbase.", correo);
            return Unauthorized(new
            {
                mensaje = "Credenciales inválidas. Verifique su correo y contraseña."
            });
        }

        if (!cuenta.Activa)
        {
            _logger.LogWarning("Intento de acceso con cuenta deshabilitada: '{Correo}'.", correo);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "La cuenta administrativa se encuentra deshabilitada."
            });
        }

        if (!string.Equals(cuenta.Rol, "admin", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Intento de acceso sin rol de administrador: '{Correo}' (rol={Rol}).", correo, cuenta.Rol);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Acceso exclusivo para administradores autorizados."
            });
        }

        bool passwordValido = false;
        try
        {
            passwordValido = !string.IsNullOrWhiteSpace(cuenta.PasswordHash)
                && BCrypt.Net.BCrypt.Verify(request.Password, cuenta.PasswordHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar el hash BCrypt de la cuenta '{Correo}'.", correo);
        }

        if (!passwordValido)
        {
            _logger.LogWarning("Contraseña incorrecta para la cuenta '{Correo}'.", correo);
            return Unauthorized(new
            {
                mensaje = "Credenciales inválidas. Verifique su correo y contraseña."
            });
        }

        var (token, expiresAtUtc, _) = _jwtService.GenerateToken(cuenta);
        _logger.LogInformation("Inicio de sesión exitoso en Couchbase Capella para '{Correo}' ({Nombre}).", cuenta.Correo, cuenta.Nombre);

        return Ok(new LoginResponse
        {
            Token = token,
            TipoToken = "Bearer",
            ExpiraEnUtc = expiresAtUtc,
            Usuario = new AdminUserDto
            {
                Correo = cuenta.Correo,
                Nombre = cuenta.Nombre,
                Rol = cuenta.Rol,
                Activa = cuenta.Activa,
                CreadaEn = cuenta.CreadaEn
            }
        });
    }

    /// <summary>
    /// Valida la sesión activa del administrador contra el token JWT y Couchbase Capella.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Me()
    {
        var correo = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrWhiteSpace(correo))
        {
            return Unauthorized(new { mensaje = "Token sin identidad válida." });
        }

        var cuenta = await _dbService.GetCuentaByCorreoAsync(correo);
        if (cuenta is null || !cuenta.Activa)
        {
            return Unauthorized(new { mensaje = "La cuenta administrativa ya no está activa." });
        }

        return Ok(new AdminUserDto
        {
            Correo = cuenta.Correo,
            Nombre = cuenta.Nombre,
            Rol = cuenta.Rol,
            Activa = cuenta.Activa,
            CreadaEn = cuenta.CreadaEn
        });
    }

    /// <summary>
    /// Cierra la sesión actual e invalida el token JWT en el servidor para impedir su reutilización.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);
        var correo = User.FindFirstValue(ClaimTypes.Email) ?? "admin";

        var expiresAtUtc = DateTime.UtcNow.AddHours(2);
        if (long.TryParse(expClaim, out var expSeconds))
        {
            expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
        }

        if (!string.IsNullOrWhiteSpace(jti))
        {
            _revocationService.RevokeToken(jti, expiresAtUtc);
        }

        _logger.LogInformation("Cierre de sesión registrado y token revocado para '{Correo}'.", correo);
        return Ok(new
        {
            mensaje = "Sesión cerrada correctamente. El token de autenticación ha sido invalidado."
        });
    }
}
