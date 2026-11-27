export interface AdminUser {
  correo: string;
  nombre: string;
  rol: string;
  activa: boolean;
  creada_en: string;
}

export interface LoginRequest {
  correo: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  tipo_token: string;
  expira_en_utc: string;
  usuario: AdminUser;
}

export interface ConteoCategoria {
  categoria: string;
  total: number;
}

export interface UsuarioAtipico {
  id_usuario: string;
  mensajes_30d: string;
  reacciones_30d: string;
}

export interface BitacoraCarga {
  id_ejecucion: string;
  archivo: string;
  fecha_ejecucion: string;
  recibidos: number;
  aceptados: number;
  transformados: number;
  rechazados: number;
}

export interface FilaCalorTemaCanal {
  tema: string;
  celdas: number[];
}

export interface CuarentenaMuestra {
  id_interaccion: string;
  id_usuario: string;
  canal: string;
  tema: string;
  motivo_rechazo: string;
}

export interface InteraccionResumen {
  id_interaccion: string;
  id_usuario: string;
  fecha_hora: string;
  canal: string;
  tipo: string;
  tema: string;
  texto: string | null;
  estado: string | null;
  sentimiento: number;
  dispositivo?: string;
  longitud_texto?: number | null;
  reacciones_recibidas: number;
  etiquetas: string[];
}

export interface CrearInteraccionPayload {
  id_usuario: string;
  canal: string;
  tipo: string;
  tema: string;
  texto: string | null;
  sentimiento: number;
  dispositivo: string;
  reacciones_recibidas: number;
  etiquetas: string[];
}

export interface ActualizarInteraccionPayload {
  canal: string;
  tipo: string;
  tema: string;
  texto: string | null;
  sentimiento: number;
  dispositivo: string;
  reacciones_recibidas: number;
  etiquetas: string[];
}

export interface ListaInteraccionesResponse {
  total: number;
  datos: InteraccionResumen[];
}

export interface DashboardResumen {
  couchbase_conectado: boolean;
  cassandra_conectado: boolean;
  total_usuarios: number;
  usuarios_con_consentimiento: number;
  usuarios_sin_consentimiento: number;
  sin_consentimiento_con_ubicacion: number;
  total_cuarentena: number;
  total_bitacora_carga: number;
  total_cuentas_admin: number;
  total_interacciones_recibidas: number;
  total_interacciones_validas: number;
  total_interacciones_usuario: number;
  total_interacciones_tema: number;
  total_interacciones_estado: number;
  total_interacciones_etiqueta: number;
  porcentaje_reacciones: number;
  tema_lider: string;
  valores_geo_borrados_usuarios: number;
  valores_geo_borrados_huerfanas: number;
  usuarios_por_estatus: ConteoCategoria[];
  usuarios_por_estado: ConteoCategoria[];
  interacciones_por_tema: ConteoCategoria[];
  interacciones_por_tipo: ConteoCategoria[];
  interacciones_por_canal: ConteoCategoria[];
  interacciones_por_etiqueta_top: ConteoCategoria[];
  mapa_calor_dia_franja: number[][];
  canales_calor: string[];
  mapa_calor_tema_canal: FilaCalorTemaCanal[];
  tendencia_diaria: ConteoCategoria[];
  muestra_cuarentena: CuarentenaMuestra[];
  ejemplo_usuario_anonimizado: any;
  atipicos_actividad: UsuarioAtipico[];
  bitacora_carga: BitacoraCarga[];
  interacciones_recientes: InteraccionResumen[];
}
