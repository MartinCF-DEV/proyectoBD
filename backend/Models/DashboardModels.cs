using System.Text.Json.Serialization;

namespace Community360.Api.Models;

public sealed class DashboardResumenDto
{
    [JsonPropertyName("couchbase_conectado")]
    public bool CouchbaseConectado { get; set; }

    [JsonPropertyName("cassandra_conectado")]
    public bool CassandraConectado { get; set; }

    [JsonPropertyName("total_usuarios")]
    public long TotalUsuarios { get; set; }

    [JsonPropertyName("usuarios_con_consentimiento")]
    public long UsuariosConConsentimiento { get; set; }

    [JsonPropertyName("usuarios_sin_consentimiento")]
    public long UsuariosSinConsentimiento { get; set; }

    [JsonPropertyName("sin_consentimiento_con_ubicacion")]
    public long SinConsentimientoConUbicacion { get; set; }

    [JsonPropertyName("total_cuarentena")]
    public long TotalCuarentena { get; set; }

    [JsonPropertyName("total_bitacora_carga")]
    public long TotalBitacoraCarga { get; set; }

    [JsonPropertyName("total_cuentas_admin")]
    public long TotalCuentasAdmin { get; set; }

    [JsonPropertyName("total_interacciones_recibidas")]
    public long TotalInteraccionesRecibidas { get; set; }

    [JsonPropertyName("total_interacciones_validas")]
    public long TotalInteraccionesValidas { get; set; }

    [JsonPropertyName("total_interacciones_usuario")]
    public long TotalInteraccionesUsuario { get; set; }

    [JsonPropertyName("total_interacciones_tema")]
    public long TotalInteraccionesTema { get; set; }

    [JsonPropertyName("total_interacciones_estado")]
    public long TotalInteraccionesEstado { get; set; }

    [JsonPropertyName("total_interacciones_etiqueta")]
    public long TotalInteraccionesEtiqueta { get; set; }

    [JsonPropertyName("porcentaje_reacciones")]
    public double PorcentajeReacciones { get; set; }

    [JsonPropertyName("tema_lider")]
    public string TemaLider { get; set; } = string.Empty;

    [JsonPropertyName("valores_geo_borrados_usuarios")]
    public long ValoresGeoBorradosUsuarios { get; set; }

    [JsonPropertyName("valores_geo_borrados_huerfanas")]
    public long ValoresGeoBorradosHuerfanas { get; set; }

    [JsonPropertyName("usuarios_por_estatus")]
    public List<ConteoCategoriaDto> UsuariosPorEstatus { get; set; } = [];

    [JsonPropertyName("usuarios_por_estado")]
    public List<ConteoCategoriaDto> UsuariosPorEstado { get; set; } = [];

    [JsonPropertyName("interacciones_por_tema")]
    public List<ConteoCategoriaDto> InteraccionesPorTema { get; set; } = [];

    [JsonPropertyName("interacciones_por_tipo")]
    public List<ConteoCategoriaDto> InteraccionesPorTipo { get; set; } = [];

    [JsonPropertyName("interacciones_por_canal")]
    public List<ConteoCategoriaDto> InteraccionesPorCanal { get; set; } = [];

    [JsonPropertyName("interacciones_por_etiqueta_top")]
    public List<ConteoCategoriaDto> InteraccionesPorEtiquetaTop { get; set; } = [];

    [JsonPropertyName("mapa_calor_dia_franja")]
    public List<List<int>> MapaCalorDiaFranja { get; set; } = [];

    [JsonPropertyName("canales_calor")]
    public List<string> CanalesCalor { get; set; } = [];

    [JsonPropertyName("mapa_calor_tema_canal")]
    public List<FilaCalorTemaCanalDto> MapaCalorTemaCanal { get; set; } = [];

    [JsonPropertyName("tendencia_diaria")]
    public List<ConteoCategoriaDto> TendenciaDiaria { get; set; } = [];

    [JsonPropertyName("muestra_cuarentena")]
    public List<CuarentenaMuestraDto> MuestraCuarentena { get; set; } = [];

    [JsonPropertyName("ejemplo_usuario_anonimizado")]
    public UsuarioPerfilDto? EjemploUsuarioAnonimizado { get; set; }

    [JsonPropertyName("atipicos_actividad")]
    public List<UsuarioAtipicoDto> AtipicosActividad { get; set; } = [];

    [JsonPropertyName("bitacora_carga")]
    public List<BitacoraCargaDto> BitacoraCarga { get; set; } = [];

    [JsonPropertyName("interacciones_recientes")]
    public List<InteraccionResumenDto> InteraccionesRecientes { get; set; } = [];
}

public sealed class FilaCalorTemaCanalDto
{
    [JsonPropertyName("tema")]
    public string Tema { get; set; } = string.Empty;

    [JsonPropertyName("celdas")]
    public List<int> Celdas { get; set; } = [];
}

public sealed class CuarentenaMuestraDto
{
    [JsonPropertyName("id_interaccion")]
    public string IdInteraccion { get; set; } = string.Empty;

    [JsonPropertyName("id_usuario")]
    public string IdUsuario { get; set; } = string.Empty;

    [JsonPropertyName("canal")]
    public string Canal { get; set; } = string.Empty;

    [JsonPropertyName("tema")]
    public string Tema { get; set; } = string.Empty;

    [JsonPropertyName("motivo_rechazo")]
    public string MotivoRechazo { get; set; } = string.Empty;
}

public sealed class UsuarioAtipicoDto
{
    [JsonPropertyName("id_usuario")]
    public string IdUsuario { get; set; } = string.Empty;

    [JsonPropertyName("mensajes_30d")]
    public string Mensajes30d { get; set; } = "—";

    [JsonPropertyName("reacciones_30d")]
    public string Reacciones30d { get; set; } = "—";
}

public sealed class ConteoCategoriaDto
{
    [JsonPropertyName("categoria")]
    public string Categoria { get; set; } = string.Empty;

    [JsonPropertyName("total")]
    public long Total { get; set; }
}

public sealed class BitacoraCargaDto
{
    [JsonPropertyName("id_ejecucion")]
    public string IdEjecucion { get; set; } = string.Empty;

    [JsonPropertyName("archivo")]
    public string Archivo { get; set; } = string.Empty;

    [JsonPropertyName("fecha_ejecucion")]
    public string FechaEjecucion { get; set; } = string.Empty;

    [JsonPropertyName("recibidos")]
    public long Recibidos { get; set; }

    [JsonPropertyName("aceptados")]
    public long Aceptados { get; set; }

    [JsonPropertyName("transformados")]
    public long Transformados { get; set; }

    [JsonPropertyName("rechazados")]
    public long Rechazados { get; set; }
}

public sealed class InteraccionResumenDto
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

    [JsonPropertyName("tema")]
    public string Tema { get; set; } = string.Empty;

    [JsonPropertyName("texto")]
    public string? Texto { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("sentimiento")]
    public float Sentimiento { get; set; }

    [JsonPropertyName("reacciones_recibidas")]
    public int ReaccionesRecibidas { get; set; }

    [JsonPropertyName("etiquetas")]
    public List<string> Etiquetas { get; set; } = [];
}
