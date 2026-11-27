using System.Text.Json.Serialization;

namespace Community360.Api.Models;

public sealed class InteraccionDetalleDto
{
    [JsonPropertyName("id_interaccion")]
    public string IdInteraccion { get; set; } = string.Empty;

    [JsonPropertyName("id_usuario")]
    public string IdUsuario { get; set; } = string.Empty;

    [JsonPropertyName("fecha_hora")]
    public string FechaHora { get; set; } = string.Empty;

    [JsonPropertyName("canal")]
    public string Canal { get; set; } = string.Empty;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("texto")]
    public string? Texto { get; set; }

    [JsonPropertyName("tema")]
    public string Tema { get; set; } = string.Empty;

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("sentimiento")]
    public float Sentimiento { get; set; }

    [JsonPropertyName("idioma")]
    public string Idioma { get; set; } = "es";

    [JsonPropertyName("dispositivo")]
    public string Dispositivo { get; set; } = "Web";

    [JsonPropertyName("longitud_texto")]
    public int? LongitudTexto { get; set; }

    [JsonPropertyName("reacciones_recibidas")]
    public int ReaccionesRecibidas { get; set; }

    [JsonPropertyName("etiquetas")]
    public List<string> Etiquetas { get; set; } = [];
}

public sealed class CrearInteraccionRequest
{
    [JsonPropertyName("id_interaccion")]
    public string? IdInteraccion { get; set; }

    [JsonPropertyName("id_usuario")]
    public string IdUsuario { get; set; } = string.Empty;

    [JsonPropertyName("canal")]
    public string Canal { get; set; } = string.Empty;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("tema")]
    public string Tema { get; set; } = string.Empty;

    [JsonPropertyName("texto")]
    public string? Texto { get; set; }

    [JsonPropertyName("sentimiento")]
    public float Sentimiento { get; set; }

    [JsonPropertyName("dispositivo")]
    public string Dispositivo { get; set; } = "Web";

    [JsonPropertyName("reacciones_recibidas")]
    public int ReaccionesRecibidas { get; set; }

    [JsonPropertyName("etiquetas")]
    public List<string>? Etiquetas { get; set; }
}

public sealed class ActualizarInteraccionRequest
{
    [JsonPropertyName("canal")]
    public string Canal { get; set; } = string.Empty;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("tema")]
    public string Tema { get; set; } = string.Empty;

    [JsonPropertyName("texto")]
    public string? Texto { get; set; }

    [JsonPropertyName("sentimiento")]
    public float Sentimiento { get; set; }

    [JsonPropertyName("dispositivo")]
    public string Dispositivo { get; set; } = "Web";

    [JsonPropertyName("reacciones_recibidas")]
    public int ReaccionesRecibidas { get; set; }

    [JsonPropertyName("etiquetas")]
    public List<string>? Etiquetas { get; set; }
}

public sealed class UbicacionUsuarioDto
{
    [JsonPropertyName("pais")]
    public string Pais { get; set; } = string.Empty;

    [JsonPropertyName("estado")]
    public string Estado { get; set; } = string.Empty;

    [JsonPropertyName("ciudad")]
    public string Ciudad { get; set; } = string.Empty;

    [JsonPropertyName("latitud")]
    public double? Latitud { get; set; }

    [JsonPropertyName("longitud")]
    public double? Longitud { get; set; }

    [JsonPropertyName("zona_horaria")]
    public string ZonaHoraria { get; set; } = string.Empty;
}

public sealed class UsuarioPerfilDto
{
    [JsonPropertyName("id_usuario")]
    public string IdUsuario { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("fecha_ingreso")]
    public string FechaIngreso { get; set; } = string.Empty;

    [JsonPropertyName("rango_edad")]
    public string RangoEdad { get; set; } = string.Empty;

    [JsonPropertyName("especie_fursona")]
    public string EspecieFursona { get; set; } = string.Empty;

    [JsonPropertyName("interes_principal")]
    public string InteresPrincipal { get; set; } = string.Empty;

    [JsonPropertyName("plataforma_principal")]
    public string PlataformaPrincipal { get; set; } = string.Empty;

    [JsonPropertyName("es_artista")]
    public bool EsArtista { get; set; }

    [JsonPropertyName("mensajes_30d")]
    public int Mensajes30d { get; set; }

    [JsonPropertyName("reacciones_30d")]
    public int Reacciones30d { get; set; }

    [JsonPropertyName("eventos_asistidos")]
    public int EventosAsistidos { get; set; }

    [JsonPropertyName("comisiones_compradas")]
    public int ComisionesCompradas { get; set; }

    [JsonPropertyName("sentimiento_promedio")]
    public double SentimientoPromedio { get; set; }

    [JsonPropertyName("ultima_actividad")]
    public string UltimaActividad { get; set; } = string.Empty;

    [JsonPropertyName("estatus")]
    public string Estatus { get; set; } = string.Empty;

    [JsonPropertyName("consentimiento_datos")]
    public string ConsentimientoDatos { get; set; } = "no";

    [JsonPropertyName("ubicacion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UbicacionUsuarioDto? Ubicacion { get; set; }
}

public static class ValidadorInteracciones
{
    public static readonly HashSet<string> CanalesValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "Telegram", "Discord", "TikTok", "Instagram", "Facebook", "Twitter/X"
    };

    public static readonly HashSet<string> TiposValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "mensaje", "comentario", "reaccion", "respuesta_encuesta", "rsvp_evento"
    };

    public static readonly HashSet<string> TemasValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "arte_comisiones", "eventos_convenciones", "fursuits", "musica",
        "reglas_moderacion", "seguridad_privacidad", "videojuegos", "humor_memes", "comunidad"
    };

    public static readonly HashSet<string> DispositivosValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "Android", "iOS", "Web", "Escritorio"
    };

    public static readonly HashSet<string> EtiquetasValidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "humor", "evento", "aviso", "arte", "musica", "reglas", "juegos"
    };

    public static List<string> ValidarDatos(
        string canal,
        string tipo,
        string tema,
        string? texto,
        float sentimiento,
        string dispositivo,
        int reaccionesRecibidas,
        List<string>? etiquetas)
    {
        var errores = new List<string>();

        if (string.IsNullOrWhiteSpace(canal) || !CanalesValidos.Contains(canal.Trim()))
            errores.Add($"El canal '{canal}' no es válido. Valores permitidos: {string.Join(", ", CanalesValidos)}.");

        if (string.IsNullOrWhiteSpace(tipo) || !TiposValidos.Contains(tipo.Trim()))
            errores.Add($"El tipo '{tipo}' no es válido. Valores permitidos: {string.Join(", ", TiposValidos)}.");

        if (string.IsNullOrWhiteSpace(tema))
            errores.Add("El tema es obligatorio.");

        if (sentimiento < -1.0f || sentimiento > 1.0f || float.IsNaN(sentimiento))
            errores.Add("El sentimiento debe estar dentro del rango [-1.0, 1.0].");

        if (reaccionesRecibidas < 0)
            errores.Add("El número de reacciones recibidas debe ser un entero mayor o igual a 0.");

        if (!string.IsNullOrWhiteSpace(dispositivo) && !DispositivosValidos.Contains(dispositivo.Trim()))
            errores.Add($"El dispositivo '{dispositivo}' no es válido. Valores permitidos: {string.Join(", ", DispositivosValidos)}.");

        var tipoNorm = tipo?.Trim().ToLowerInvariant() ?? "";
        if (tipoNorm != "reaccion" && string.IsNullOrWhiteSpace(texto))
            errores.Add("El campo 'texto' es obligatorio para interacciones distintas a 'reaccion'.");

        if (etiquetas is { Count: > 3 })
            errores.Add("Una interacción puede tener como máximo 3 etiquetas.");

        if (etiquetas is not null)
        {
            foreach (var et in etiquetas)
            {
                if (!string.IsNullOrWhiteSpace(et) && !EtiquetasValidas.Contains(et.Trim()))
                {
                    errores.Add($"La etiqueta '{et}' no pertenece al catálogo permitido ({string.Join(", ", EtiquetasValidas)}).");
                }
            }
        }

        return errores;
    }
}
