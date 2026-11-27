using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Community360.Api.Configuration;
using Community360.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace Community360.Api.Services;

public sealed class JwtTokenService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expMinutes;

    public JwtTokenService()
    {
        _secret = Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? "C360_ClaveSecreta_SuperSegura_JWT_2026_UTM_BasesDatosNube!";
        _issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "Community360.Api";
        _audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "Community360.Web";
        _expMinutes = int.TryParse(Environment.GetEnvironmentVariable("JWT_EXP_MINUTES"), out var m) && m > 0
            ? m
            : 120;
    }

    public (string Token, DateTime ExpiresAtUtc, string Jti) GenerateToken(CuentaAdminDocument cuenta)
    {
        var jti = Guid.NewGuid().ToString("N");
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_expMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, cuenta.Correo),
            new(JwtRegisteredClaimNames.Email, cuenta.Correo),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTypes.NameIdentifier, cuenta.Correo),
            new(ClaimTypes.Email, cuenta.Correo),
            new(ClaimTypes.Name, cuenta.Nombre),
            new(ClaimTypes.Role, cuenta.Rol),
            new("rol", cuenta.Rol),
            new("nombre", cuenta.Nombre)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var descriptor = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(descriptor);
        return (tokenString, expiresAtUtc, jti);
    }
}
