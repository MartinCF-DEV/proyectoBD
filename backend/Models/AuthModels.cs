using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Community360.Api.Models;

/// <summary>
/// Esquema documental de la colección 'cuentas' en Couchbase Capella (Fase 1, Sección 4.2 y Tabla 8).
/// Clave del documento: correo.
/// </summary>
public sealed class CuentaAdminDocument
{
    [JsonPropertyName("correo")]
    public string Correo { get; set; } = string.Empty;

    [JsonPropertyName("usuario")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Usuario { get; set; }

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [JsonPropertyName("rol")]
    public string Rol { get; set; } = "admin";

    [JsonPropertyName("activa")]
    public bool Activa { get; set; } = true;

    [JsonPropertyName("creada_en")]
    public string CreadaEn { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "El usuario o correo electrónico es obligatorio.")]
    [JsonPropertyName("correo")]
    public string Correo { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public sealed class AdminUserDto
{
    [JsonPropertyName("correo")]
    public string Correo { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("rol")]
    public string Rol { get; set; } = "admin";

    [JsonPropertyName("activa")]
    public bool Activa { get; set; }

    [JsonPropertyName("creada_en")]
    public string CreadaEn { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("tipo_token")]
    public string TipoToken { get; set; } = "Bearer";

    [JsonPropertyName("expira_en_utc")]
    public DateTime ExpiraEnUtc { get; set; }

    [JsonPropertyName("usuario")]
    public AdminUserDto Usuario { get; set; } = new();
}
