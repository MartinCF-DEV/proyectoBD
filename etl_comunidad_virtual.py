#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
COMMUNITY 360 - Fase 2: limpieza, procesamiento y carga en la nube (ETL)

Ejecuta, en orden, las 8 etapas pedidas sobre los dos CSV originales:
  1 duplicados | 2 nulos | 3 estandarización | 4 tipos | 5 referencias
  6 privacidad geográfica | 7 exportación depurada | 8 carga (Cassandra / Couchbase)

Uso:
  python etl_comunidad_virtual.py --usuarios datos/comunidad_virtual_usuarios.csv \
      --interacciones datos/comunidad_virtual_1000_interacciones.csv --salida salida
  python etl_comunidad_virtual.py ... --cargar ambos        # etapa 8
  python etl_comunidad_virtual.py ... --verificar ambos     # conteos y consulta de privacidad

Las credenciales se leen de variables de entorno (o archivo .env); nunca van en el código.
"""
import argparse
import json
import logging
import os
from datetime import datetime, timezone
from pathlib import Path
from zoneinfo import available_timezones

import numpy as np
import pandas as pd

log = logging.getLogger("etl")


def cargar_dotenv(ruta=".env"):
    """Carga variables desde un archivo .env local sin requerir librerías externas."""
    p = Path(ruta)
    if not p.is_file():
        p = Path(__file__).resolve().parent / ruta
    if not p.is_file():
        return
    for linea in p.read_text(encoding="utf-8").splitlines():
        linea = linea.strip()
        if not linea or linea.startswith("#") or "=" not in linea:
            continue
        k, v = linea.split("=", 1)
        os.environ.setdefault(k.strip(), v.strip().strip("'\""))


# --------------------------------------------------------------------------
# Configuración
# --------------------------------------------------------------------------
NULOS = {"", "null", "none", "nan", "n/a", "na", "-", "--", "s/d", "sin dato"}

# Columnas geográficas que se eliminan cuando consentimiento_datos = no.
# zona_horaria revela región, por eso se trata como dato geográfico.
GEO_USUARIOS = ["pais", "estado", "ciudad", "latitud", "longitud", "zona_horaria"]
GEO_INTERACCIONES = ["pais", "estado", "ciudad"]

CATEGORIAS_USUARIOS = ["pais", "estado", "ciudad", "rango_edad", "especie_fursona",
                       "interes_principal", "plataforma_principal"]
MINUSCULAS_USUARIOS = ["estatus", "consentimiento_datos", "es_artista"]
CATEGORIAS_INTERACCIONES = ["canal", "pais", "estado", "ciudad", "dispositivo"]
MINUSCULAS_INTERACCIONES = ["tipo", "tema", "idioma"]

# columna: (tipo, obligatorio, (mín, máx) opcional)
ESQUEMA_USUARIOS = {
    "id_usuario": ("str", True, None), "alias": ("str", True, None),
    "fecha_ingreso": ("date", True, None),
    "pais": ("str", False, None), "estado": ("str", False, None), "ciudad": ("str", False, None),
    "latitud": ("float", False, (-90, 90)), "longitud": ("float", False, (-180, 180)),
    "zona_horaria": ("tz", False, None),
    "rango_edad": ("str", False, None), "especie_fursona": ("str", False, None),
    "interes_principal": ("str", False, None), "plataforma_principal": ("str", False, None),
    "es_artista": ("bool", True, None),
    "mensajes_30d": ("int", True, (0, None)), "reacciones_30d": ("int", True, (0, None)),
    "eventos_asistidos": ("int", True, (0, None)), "comisiones_compradas": ("int", True, (0, None)),
    "sentimiento_promedio": ("float", False, (-1, 1)),
    "ultima_actividad": ("date", False, None),
    "estatus": ("str", True, None), "consentimiento_datos": ("bool", True, None),
}
ESQUEMA_INTERACCIONES = {
    "id_interaccion": ("str", True, None), "id_usuario": ("str", True, None),
    "timestamp": ("ts", True, None),
    "canal": ("str", True, None), "tipo": ("str", True, None), "texto": ("str", False, None),
    "tema": ("str", False, None), "sentimiento": ("float", False, (-1, 1)),
    "idioma": ("str", False, None),
    "pais": ("str", False, None), "estado": ("str", False, None), "ciudad": ("str", False, None),
    "dispositivo": ("str", False, None),
    "longitud_texto": ("int", False, (0, None)), "reacciones_recibidas": ("int", True, (0, None)),
}


# --------------------------------------------------------------------------
# Utilidades
# --------------------------------------------------------------------------
def leer_csv(ruta):
    return pd.read_csv(ruta, dtype=str, keep_default_na=False, encoding="utf-8-sig")


def canonizar(serie):
    """Unifica variantes de escritura (mayúsculas, espacios) a la forma más frecuente, preservando nulos."""
    s = serie.str.replace(r"\s+", " ", regex=True).str.strip()
    validos = s.dropna()
    if validos.empty:
        return s
    clave = validos.str.casefold()
    forma = validos.groupby(clave).agg(lambda x: x.value_counts().idxmax())
    res = s.copy()
    res.loc[validos.index] = clave.map(forma)
    return res


def convertir(serie, tipo):
    """Devuelve (serie_tipada, máscara_de_inválidos). Los nulos no son inválidos."""
    if tipo in ("int", "float"):
        x = pd.to_numeric(serie, errors="coerce")
        malo = serie.notna() & x.isna()
        if tipo == "int":
            malo |= serie.notna() & x.notna() & (x % 1 != 0)
            return x.where(~malo).astype("Int64"), malo
        return x.where(~malo).astype("Float64"), malo
    if tipo == "bool":
        m = serie.str.lower().map({"1": True, "0": False, "true": True, "false": False,
                                   "si": True, "sí": True, "no": False})
        return m.astype("boolean"), serie.notna() & m.isna()
    if tipo == "date":
        d = pd.to_datetime(serie, format="%Y-%m-%d", errors="coerce")
        return d, serie.notna() & d.isna()
    if tipo == "ts":
        d = pd.to_datetime(serie, format="%Y-%m-%dT%H:%M:%SZ", errors="coerce", utc=True)
        return d, serie.notna() & d.isna()
    if tipo == "tz":
        ok = serie.isin(available_timezones())
        return serie, serie.notna() & ~ok
    return serie, pd.Series(False, index=serie.index)


# --------------------------------------------------------------------------
# Etapas 1-7
# --------------------------------------------------------------------------
def etapa1_duplicados(u, i, aud):
    log.info("Etapa 1: duplicados")
    for c in ("id_usuario", "alias"):
        u[c] = u[c].str.strip()
    i["id_interaccion"] = i["id_interaccion"].str.strip().str.upper()
    u["id_usuario"] = u["id_usuario"].str.upper()
    i["id_usuario"] = i["id_usuario"].str.strip().str.upper()

    res = {}
    # usuarios: fila completa, id_usuario, alias (sin distinguir mayúsculas, ignorando vacíos)
    n0 = len(u)
    u = u.drop_duplicates()
    res["usuarios_fila_completa"] = n0 - len(u)

    n0 = len(u)
    id_valido = ~u["id_usuario"].str.lower().isin(NULOS)
    u = u[~(id_valido & u["id_usuario"].duplicated(keep="first"))]
    res["usuarios_id_repetido"] = n0 - len(u)

    n0 = len(u)
    alias_valido = ~u["alias"].str.lower().isin(NULOS)
    u = u[~(alias_valido & u["alias"].str.casefold().duplicated(keep="first"))]
    res["usuarios_alias_repetido"] = n0 - len(u)

    # interacciones: fila completa, id_interaccion
    n0 = len(i)
    i = i.drop_duplicates()
    res["interacciones_fila_completa"] = n0 - len(i)

    n0 = len(i)
    id_int_valido = ~i["id_interaccion"].str.lower().isin(NULOS)
    i = i[~(id_int_valido & i["id_interaccion"].duplicated(keep="first"))]
    res["interacciones_id_repetido"] = n0 - len(i)

    aud["etapa1_duplicados_eliminados"] = res
    return u.reset_index(drop=True), i.reset_index(drop=True)


def etapa2_nulos(u, i, aud):
    log.info("Etapa 2: valores vacíos y nulos")
    res = {"usuarios": {}, "interacciones": {}}
    for nombre, df in (("usuarios", u), ("interacciones", i)):
        for c in df.columns:
            df[c] = df[c].str.strip()
            vacio = df[c].str.lower().isin(NULOS)
            if vacio.sum():
                res[nombre][c] = int(vacio.sum())
            df[c] = df[c].where(~vacio, pd.NA)
    # reacciones sin texto: texto nulo es válido para reacciones; longitud_texto se normaliza a nulo
    sin_texto = i["texto"].isna()
    incons = sin_texto & i["longitud_texto"].notna()
    i.loc[sin_texto, "longitud_texto"] = pd.NA
    res["interacciones_longitud_texto_normalizada_a_nulo"] = int(incons.sum())
    aud["etapa2_nulos_normalizados"] = res
    return u, i


def etapa3_estandarizar(u, i, aud):
    log.info("Etapa 3: estandarización")
    res = {"valores_unificados": {}, "fechas_convertidas": {}}

    for df, nombre, cats, mins in ((u, "usuarios", CATEGORIAS_USUARIOS, MINUSCULAS_USUARIOS),
                                   (i, "interacciones", CATEGORIAS_INTERACCIONES, MINUSCULAS_INTERACCIONES)):
        for c in cats:
            nuevo = canonizar(df[c])
            cambios = int(((df[c] != nuevo) & df[c].notna()).sum())
            if cambios:
                res["valores_unificados"][f"{nombre}.{c}"] = cambios
            df[c] = nuevo
        for c in mins:
            df[c] = df[c].str.lower()

    # fechas dd/mm/aaaa -> aaaa-mm-dd
    for c in ("fecha_ingreso", "ultima_actividad"):
        d = pd.to_datetime(u[c], format="%d/%m/%Y", errors="coerce")
        res["fechas_convertidas"][f"usuarios.{c}"] = int(d.notna().sum())
        u[c] = d.dt.strftime("%Y-%m-%d").where(d.notna(), u[c])
    # timestamps -> UTC ISO 8601 con sufijo Z
    t = pd.to_datetime(i["timestamp"], utc=True, errors="coerce", format="ISO8601")
    res["fechas_convertidas"]["interacciones.timestamp"] = int(t.notna().sum())
    i["timestamp"] = t.dt.strftime("%Y-%m-%dT%H:%M:%SZ").where(t.notna(), i["timestamp"])

    # campo multivalor: etiquetas "a|b|c" -> lista sin repetidos
    def separar(v):
        if pd.isna(v):
            return None
        vistas = []
        for p in pd.Series(v.lower()).str.split(r"[|;,]", regex=True).iloc[0]:
            p = p.strip()
            if p and p not in vistas:
                vistas.append(p)
        return vistas or None

    i["etiquetas"] = i["etiquetas"].map(separar)
    res["interacciones_con_etiquetas"] = int(i["etiquetas"].notna().sum())
    aud["etapa3_estandarizacion"] = res
    return u, i


def _validar(df, esquema, nombre, aud_res):
    """Etapa 4: conversión estricta. Los registros inválidos se separan con su motivo."""
    motivos = pd.Series("", index=df.index)
    out = df.copy()
    for col, (tipo, obligatorio, rango) in esquema.items():
        serie, malo = convertir(df[col], tipo)
        out[col] = serie
        if obligatorio:
            falta = df[col].isna()
            motivos[falta] += f"{col}:obligatorio_vacio;"
        motivos[malo] += f"{col}:tipo_{tipo}_invalido;"
        if rango and tipo in ("int", "float"):
            lo, hi = rango
            fuera = pd.Series(False, index=df.index)
            if lo is not None:
                fuera |= serie.notna() & (serie < lo)
            if hi is not None:
                fuera |= serie.notna() & (serie > hi)
            motivos[fuera] += f"{col}:fuera_de_rango;"
    # validación adicional de negocio: texto y longitud_texto obligatorios salvo cuando tipo == 'reaccion'
    if nombre == "interacciones":
        no_reaccion = df["tipo"].fillna("") != "reaccion"
        falta_texto = no_reaccion & df["texto"].isna()
        motivos[falta_texto] += "texto:obligatorio_fuera_de_reaccion;"
        falta_long = no_reaccion & df["longitud_texto"].isna()
        motivos[falta_long] += "longitud_texto:obligatorio_fuera_de_reaccion;"
    mal = motivos != ""
    rechazados = df[mal].assign(motivo_rechazo=motivos[mal])
    aud_res[nombre] = {"validos": int((~mal).sum()), "rechazados": int(mal.sum())}
    return out[~mal].reset_index(drop=True), rechazados.reset_index(drop=True)


def etapa4_tipos(u, i, aud):
    log.info("Etapa 4: validación de tipos")
    res = {}
    u, rech_u = _validar(u, ESQUEMA_USUARIOS, "usuarios", res)
    i2, rech_i = _validar(i, ESQUEMA_INTERACCIONES, "interacciones", res)
    # atípicos: se reportan, no se eliminan (pueden ser actividad real)
    atip = {}
    for c in ("mensajes_30d", "reacciones_30d"):
        p99 = float(u[c].astype(float).quantile(0.99))
        top = u[u[c].astype(float) > p99 * 3][["id_usuario", c]]
        atip[c] = {"p99": p99, "casos_extremos": top.astype(str).values.tolist()}
    res["atipicos_reportados"] = atip
    aud["etapa4_validacion_tipos"] = res
    return u, i2, rech_u, rech_i


def etapa5_referencias(u, i, aud):
    log.info("Etapa 5: integridad referencial")
    existe = i["id_usuario"].isin(u["id_usuario"])
    huerfanas = i[~existe].copy()
    huerfanas["motivo_rechazo"] = "id_usuario_inexistente_en_usuarios"
    aud["etapa5_referencias"] = {
        "interacciones_huerfanas": int((~existe).sum()),
        "usuarios_distintos_huerfanos": int(i.loc[~existe, "id_usuario"].nunique()),
        "interacciones_validas": int(existe.sum()),
        "usuarios_sin_interacciones": int((~u["id_usuario"].isin(i["id_usuario"])).sum()),
    }
    return i[existe].reset_index(drop=True), huerfanas.reset_index(drop=True)


def etapa6_privacidad(u, i, huerfanas, aud):
    log.info("Etapa 6: eliminación de datos geográficos sin consentimiento")
    sin = ~u["consentimiento_datos"].astype(bool)
    con_geo = sin & u[GEO_USUARIOS].notna().any(axis=1)
    valores = int(u.loc[sin, GEO_USUARIOS].notna().sum().sum())
    u.loc[sin, GEO_USUARIOS] = pd.NA

    ids_sin = set(u.loc[sin, "id_usuario"])
    m = i["id_usuario"].isin(ids_sin)
    val_i = int(i.loc[m, GEO_INTERACCIONES].notna().sum().sum())
    i.loc[m, GEO_INTERACCIONES] = pd.NA

    # En interacciones huérfanas no existe el usuario para verificar su consentimiento;
    # según la Fase 1 (Tabla 3), se omite su geografía antes de enviar a cuarentena.
    huerf_con_geo = int(huerfanas[GEO_INTERACCIONES].notna().any(axis=1).sum())
    val_h = int(huerfanas[GEO_INTERACCIONES].notna().sum().sum())
    huerfanas.loc[:, GEO_INTERACCIONES] = pd.NA

    aud["etapa6_privacidad"] = {
        "usuarios_sin_consentimiento": int(sin.sum()),
        "usuarios_con_geo_depurados": int(con_geo.sum()),
        "valores_geograficos_borrados_usuarios": valores,
        "interacciones_de_usuarios_sin_consentimiento": int(m.sum()),
        "valores_geograficos_borrados_interacciones": val_i,
        "interacciones_huerfanas_con_geo_depuradas": huerf_con_geo,
        "valores_geograficos_borrados_huerfanas": val_h,
        "columnas_usuarios": GEO_USUARIOS,
        "columnas_interacciones": GEO_INTERACCIONES,
    }
    # verificación interna: no debe quedar nada
    assert u.loc[sin, GEO_USUARIOS].notna().sum().sum() == 0
    assert i.loc[i["id_usuario"].isin(ids_sin), GEO_INTERACCIONES].notna().sum().sum() == 0
    assert huerfanas[GEO_INTERACCIONES].notna().sum().sum() == 0
    return u, i, huerfanas


def _a_csv(df):
    out = df.copy()
    for c in out.columns:
        if out[c].map(lambda v: isinstance(v, list)).any():
            out[c] = out[c].map(lambda v: "|".join(v) if isinstance(v, list) else "")
    for c in out.select_dtypes(include=["datetimetz"]).columns:
        out[c] = out[c].dt.strftime("%Y-%m-%dT%H:%M:%SZ")
    for c in out.select_dtypes(include=["datetime"]).columns:
        out[c] = out[c].dt.strftime("%Y-%m-%d")
    return out


def etapa7_exportar(u, i, rechazos, salida, aud):
    log.info("Etapa 7: exportación a %s", salida)
    salida = Path(salida)
    (salida / "rechazados").mkdir(parents=True, exist_ok=True)
    archivos = {
        "usuarios_depurados.csv": _a_csv(u),
        "interacciones_depuradas.csv": _a_csv(i),
        "interaccion_etiquetas.csv": i[["id_interaccion", "etiquetas"]].explode("etiquetas")
            .dropna().rename(columns={"etiquetas": "etiqueta"}),
    }
    for nombre, df in rechazos.items():
        archivos[f"rechazados/{nombre}.csv"] = _a_csv(df)
    for nombre, df in archivos.items():
        df.to_csv(salida / nombre, index=False, encoding="utf-8-sig")
    aud["etapa7_exportacion"] = {n: len(d) for n, d in archivos.items()}
    aud["generado_utc"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    with open(salida / "auditoria.json", "w", encoding="utf-8") as f:
        json.dump(aud, f, ensure_ascii=False, indent=2)


# --------------------------------------------------------------------------
# Etapa 8: carga en la nube (Arquitectura Híbrida Fase 1)
#   - Apache Cassandra (Astra DB): 5 tablas desnormalizadas de interacciones
#   - Couchbase Capella: colecciones usuarios, cuarentena y bitacora_carga
# --------------------------------------------------------------------------
COLS_FECHA = {"fecha_ingreso", "ultima_actividad"}


def _py(v, modo, col):
    if isinstance(v, list):
        return (set(v) if modo == "cql" else v)
    if v is None or v is pd.NA or (not isinstance(v, (list, set)) and pd.isna(v)):
        return None
    if isinstance(v, pd.Timestamp):
        if col in COLS_FECHA:
            return v.date() if modo == "cql" else v.date().isoformat()
        return v.to_pydatetime() if modo == "cql" else v.strftime("%Y-%m-%dT%H:%M:%SZ")
    if isinstance(v, np.generic):
        return v.item()
    return v


def registros(df, modo):
    cols = list(df.columns)
    return [{c: _py(v, modo, c) for c, v in zip(cols, fila)}
            for fila in df.itertuples(index=False, name=None)]


DDL_CASSANDRA = [
    """CREATE TABLE IF NOT EXISTS interaccion_por_id (
        id_interaccion text PRIMARY KEY,
        id_usuario text,
        fecha_hora timestamp,
        canal text,
        tipo text,
        texto text,
        tema text,
        estado text,
        sentimiento float,
        idioma text,
        dispositivo text,
        longitud_texto int,
        reacciones_recibidas int,
        etiquetas set<text>
    )""",
    """CREATE TABLE IF NOT EXISTS interaccion_por_usuario (
        id_usuario text,
        fecha_hora timestamp,
        id_interaccion text,
        tema text,
        canal text,
        tipo text,
        sentimiento float,
        PRIMARY KEY ((id_usuario), fecha_hora, id_interaccion)
    ) WITH CLUSTERING ORDER BY (fecha_hora DESC, id_interaccion ASC)""",
    """CREATE TABLE IF NOT EXISTS interaccion_por_tema (
        tema text,
        canal text,
        fecha_hora timestamp,
        id_interaccion text,
        tipo text,
        sentimiento float,
        reacciones_recibidas int,
        PRIMARY KEY ((tema), canal, fecha_hora, id_interaccion)
    )""",
    """CREATE TABLE IF NOT EXISTS interaccion_por_estado (
        estado text,
        fecha_hora timestamp,
        id_interaccion text,
        tema text,
        sentimiento float,
        PRIMARY KEY ((estado), fecha_hora, id_interaccion)
    )""",
    """CREATE TABLE IF NOT EXISTS interaccion_por_etiqueta (
        etiqueta text,
        fecha_hora timestamp,
        id_interaccion text,
        tema text,
        canal text,
        sentimiento float,
        PRIMARY KEY ((etiqueta), fecha_hora, id_interaccion)
    )""",
]


def _conectar_cassandra():
    """Conecta a DataStax Astra DB con timeout extendido (usando AsyncoreConnection vía pyasyncore)."""
    logging.getLogger("cassandra").setLevel(logging.ERROR)
    from cassandra.io.asyncorereactor import AsyncoreConnection
    from cassandra.cluster import Cluster
    from cassandra.auth import PlainTextAuthProvider

    cluster = Cluster(
        cloud={"secure_connect_bundle": os.environ["ASTRA_BUNDLE"], "connect_timeout": 30},
        auth_provider=PlainTextAuthProvider("token", os.environ["ASTRA_TOKEN"]),
        connection_class=AsyncoreConnection,
        protocol_version=4,
        connect_timeout=30,
    )
    sesion = cluster.connect()
    ks = os.environ.get("ASTRA_KEYSPACE", "default_keyspace")
    try:
        sesion.set_keyspace(ks)
    except Exception:
        log.warning("Keyspace '%s' no existe en Astra DB; usando 'default_keyspace'", ks)
        sesion.set_keyspace("default_keyspace")
    return sesion


def cargar_cassandra(i):
    """DataStax Astra DB. Variables: ASTRA_BUNDLE, ASTRA_TOKEN, ASTRA_KEYSPACE.
    Carga las 207 interacciones válidas en las 5 tablas orientadas a consulta definidas en la Fase 1."""
    sesion = _conectar_cassandra()
    from cassandra.concurrent import execute_concurrent_with_args

    for ddl in DDL_CASSANDRA:
        sesion.execute(ddl)

    df_base = i.rename(columns={"timestamp": "fecha_hora"})
    filas = registros(df_base, "cql")

    # 1. interaccion_por_id (207 filas; estado es None en las 26 sin consentimiento)
    cols_id = ["id_interaccion", "id_usuario", "fecha_hora", "canal", "tipo", "texto", "tema",
               "estado", "sentimiento", "idioma", "dispositivo", "longitud_texto",
               "reacciones_recibidas", "etiquetas"]
    datos_id = [tuple(r[c] for c in cols_id) for r in filas]

    # 2. interaccion_por_usuario (207 filas)
    cols_usr = ["id_usuario", "fecha_hora", "id_interaccion", "tema", "canal", "tipo", "sentimiento"]
    datos_usr = [tuple(r[c] for c in cols_usr) for r in filas]

    # 3. interaccion_por_tema (207 filas)
    cols_tema = ["tema", "canal", "fecha_hora", "id_interaccion", "tipo", "sentimiento", "reacciones_recibidas"]
    datos_tema = [tuple(r[c] for c in cols_tema) for r in filas]

    # 4. interaccion_por_estado (solo las 181 filas donde estado no es None)
    cols_est = ["estado", "fecha_hora", "id_interaccion", "tema", "sentimiento"]
    datos_est = [tuple(r[c] for c in cols_est) for r in filas if r["estado"] is not None]

    # 5. interaccion_por_etiqueta (318 filas desnormalizadas de las 158 interacciones con etiquetas)
    cols_etiq = ["etiqueta", "fecha_hora", "id_interaccion", "tema", "canal", "sentimiento"]
    datos_etiq = []
    for r in filas:
        if r["etiquetas"]:
            for et in sorted(r["etiquetas"]):
                datos_etiq.append((et, r["fecha_hora"], r["id_interaccion"],
                                   r["tema"], r["canal"], r["sentimiento"]))

    lotes = [
        ("interaccion_por_id", cols_id, datos_id),
        ("interaccion_por_usuario", cols_usr, datos_usr),
        ("interaccion_por_tema", cols_tema, datos_tema),
        ("interaccion_por_estado", cols_est, datos_est),
        ("interaccion_por_etiqueta", cols_etiq, datos_etiq),
    ]
    for tabla, cols, datos in lotes:
        ps = sesion.prepare(f"INSERT INTO {tabla} ({', '.join(cols)}) VALUES ({', '.join('?' * len(cols))})")
        for ok, err in execute_concurrent_with_args(sesion, ps, datos, concurrency=50, raise_on_first_error=False):
            if not ok:
                log.error("Fallo en %s: %s", tabla, err)
        log.info("Cassandra: %s cargada (%d filas)", tabla, len(datos))
    sesion.shutdown()


def _couchbase_cluster():
    logging.getLogger("couchbase").setLevel(logging.ERROR)
    from couchbase.cluster import Cluster
    from couchbase.options import ClusterOptions
    from couchbase.auth import PasswordAuthenticator
    opciones = ClusterOptions(PasswordAuthenticator(os.environ["CB_USUARIO"], os.environ["CB_CLAVE"]))
    opciones.apply_profile("wan_development")
    cluster = Cluster(os.environ["CB_CONEXION"], opciones)
    cluster.wait_until_ready(__import__("datetime").timedelta(seconds=15))
    return cluster


def _doc_usuario_couchbase(r):
    """Construye el documento JSON del usuario según la Sección 4.2 de la Fase 1."""
    consentimiento = bool(r["consentimiento_datos"])
    doc = {
        "id_usuario": r["id_usuario"],
        "alias": r["alias"],
        "fecha_ingreso": r["fecha_ingreso"],
        "rango_edad": r["rango_edad"],
        "especie_fursona": r["especie_fursona"],
        "interes_principal": r["interes_principal"],
        "plataforma_principal": r["plataforma_principal"],
        "es_artista": bool(r["es_artista"]),
        "mensajes_30d": r["mensajes_30d"],
        "reacciones_30d": r["reacciones_30d"],
        "eventos_asistidos": r["eventos_asistidos"],
        "comisiones_compradas": r["comisiones_compradas"],
        "sentimiento_promedio": r["sentimiento_promedio"],
        "ultima_actividad": r["ultima_actividad"],
        "estatus": r["estatus"],
        "consentimiento_datos": "si" if consentimiento else "no",
    }
    if consentimiento:
        doc["ubicacion"] = {
            "pais": r["pais"],
            "estado": r["estado"],
            "ciudad": r["ciudad"],
            "latitud": r["latitud"],
            "longitud": r["longitud"],
            "zona_horaria": r["zona_horaria"],
        }
    return doc


def _doc_cuarentena_couchbase(r, fecha_carga):
    """Construye el documento JSON para la colección cuarentena (sin campos geográficos)."""
    return {
        "id_interaccion": r["id_interaccion"],
        "motivo_rechazo": r["motivo_rechazo"],
        "fecha_carga": fecha_carga,
        "registro_original": {
            "id_usuario": r["id_usuario"],
            "timestamp": r["timestamp"],
            "canal": r["canal"],
            "tipo": r["tipo"],
            "texto": r["texto"],
            "tema": r["tema"],
            "sentimiento": r["sentimiento"],
            "idioma": r["idioma"],
            "dispositivo": r["dispositivo"],
            "longitud_texto": r["longitud_texto"],
            "reacciones_recibidas": r["reacciones_recibidas"],
            "etiquetas": r["etiquetas"] if r["etiquetas"] is not None else [],
        },
    }


def _preparar_couchbase(cluster, bucket_obj, nombre_bucket, nombre_scope):
    """Crea automáticamente el scope, las colecciones y los índices si no existen en Couchbase."""
    import time
    cm = bucket_obj.collections()
    scopes = [s.name for s in cm.get_all_scopes()]
    if nombre_scope not in scopes:
        log.info("Couchbase: creando scope '%s'...", nombre_scope)
        cm.create_scope(nombre_scope)
        time.sleep(2)

    existentes = {c.name for s in cm.get_all_scopes() if s.name == nombre_scope for c in s.collections}
    for col in ("usuarios", "cuarentena", "bitacora_carga", "cuentas"):
        if col not in existentes:
            log.info("Couchbase: creando colección '%s.%s'...", nombre_scope, col)
            cm.create_collection(nombre_scope, col)
            time.sleep(2)

    # Crear índices primarios y secundarios de la Fase 1 para consultas SQL++
    ddls_idx = [
        f"CREATE PRIMARY INDEX IF NOT EXISTS ON `{nombre_bucket}`.`{nombre_scope}`.`usuarios`",
        f"CREATE PRIMARY INDEX IF NOT EXISTS ON `{nombre_bucket}`.`{nombre_scope}`.`cuarentena`",
        f"CREATE PRIMARY INDEX IF NOT EXISTS ON `{nombre_bucket}`.`{nombre_scope}`.`bitacora_carga`",
        f"CREATE INDEX `idx_usuarios_estatus` IF NOT EXISTS ON `{nombre_bucket}`.`{nombre_scope}`.`usuarios`(estatus)",
        f"CREATE INDEX `idx_usuarios_estado` IF NOT EXISTS ON `{nombre_bucket}`.`{nombre_scope}`.`usuarios`(ubicacion.estado)",
        f"CREATE INDEX `idx_usuarios_interes` IF NOT EXISTS ON `{nombre_bucket}`.`{nombre_scope}`.`usuarios`(interes_principal)",
        f"CREATE INDEX `idx_cuarentena_motivo` IF NOT EXISTS ON `{nombre_bucket}`.`{nombre_scope}`.`cuarentena`(motivo_rechazo)",
    ]
    for q in ddls_idx:
        try:
            list(cluster.query(q))
        except Exception as e:
            log.warning("Aviso al crear índice (%s): %s", q, e)


def cargar_couchbase(u, huerfanas, aud, ruta_usuarios, ruta_interacciones):
    """Couchbase Capella. Variables: CB_CONEXION, CB_USUARIO, CB_CLAVE, CB_BUCKET, CB_SCOPE.
    Carga las colecciones 'usuarios', 'cuarentena' y 'bitacora_carga' según la Fase 1."""
    from datetime import timedelta
    from couchbase.options import UpsertMultiOptions

    cluster = _couchbase_cluster()
    nombre_bucket = os.environ.get("CB_BUCKET", "community360")
    nombre_scope = os.environ.get("CB_SCOPE", "comunidad")
    bucket_obj = cluster.bucket(nombre_bucket)
    _preparar_couchbase(cluster, bucket_obj, nombre_bucket, nombre_scope)

    scope = bucket_obj.scope(nombre_scope)
    ahora = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    def _cargar_lotes(col, mapa_docs, tam_lote=200):
        llaves = list(mapa_docs.keys())
        for ini in range(0, len(llaves), tam_lote):
            sub = {k: mapa_docs[k] for k in llaves[ini:ini + tam_lote]}
            try:
                col.upsert_multi(sub, UpsertMultiOptions(timeout=timedelta(seconds=30)))
            except Exception:
                for k, v in sub.items():
                    col.upsert(k, v)

    # 1. Colección usuarios (clave: id_usuario)
    col_u = scope.collection("usuarios")
    docs_u = {r["id_usuario"]: _doc_usuario_couchbase(r) for r in registros(u, "json")}
    _cargar_lotes(col_u, docs_u)
    log.info("Couchbase: colección usuarios cargada (%d documentos)", len(docs_u))

    # 2. Colección cuarentena (clave: id_interaccion)
    col_c = scope.collection("cuarentena")
    docs_c = {r["id_interaccion"]: _doc_cuarentena_couchbase(r, ahora) for r in registros(huerfanas, "json")}
    _cargar_lotes(col_c, docs_c)
    log.info("Couchbase: colección cuarentena cargada (%d documentos)", len(docs_c))

    # 3. Colección bitacora_carga (clave: id_ejecucion fija por archivo para ser idempotente)
    col_b = scope.collection("bitacora_carga")
    try:
        list(cluster.query(f"DELETE FROM `{nombre_bucket}`.`{nombre_scope}`.`bitacora_carga`"))
    except Exception:
        pass
    bitacoras = [
        {
            "id_ejecucion": "ETL-USUARIOS",
            "archivo": Path(ruta_usuarios).name,
            "fecha_ejecucion": ahora,
            "recibidos": aud["entrada"]["usuarios"],
            "aceptados": aud["salida"]["usuarios"],
            "transformados": aud["salida"]["usuarios"],
            "rechazados": aud["entrada"]["usuarios"] - aud["salida"]["usuarios"],
        },
        {
            "id_ejecucion": "ETL-INTERACCIONES",
            "archivo": Path(ruta_interacciones).name,
            "fecha_ejecucion": ahora,
            "recibidos": aud["entrada"]["interacciones"],
            "aceptados": aud["salida"]["interacciones"],
            "transformados": aud["salida"]["interacciones"],
            "rechazados": aud["entrada"]["interacciones"] - aud["salida"]["interacciones"],
        },
    ]
    for b in bitacoras:
        col_b.upsert(b["id_ejecucion"], b)
    log.info("Couchbase: colección bitacora_carga actualizada (%d documentos)", len(bitacoras))


def verificar_cassandra():
    s = _conectar_cassandra()
    tablas = (
        "interaccion_por_id",
        "interaccion_por_usuario",
        "interaccion_por_tema",
        "interaccion_por_estado",
        "interaccion_por_etiqueta",
    )
    for t in tablas:
        print(f"Cassandra - {t}:", s.execute(f"SELECT COUNT(*) FROM {t}").one()[0])
    s.shutdown()


def verificar_couchbase():
    cl = _couchbase_cluster()
    b = os.environ.get("CB_BUCKET", "community360")
    sc = os.environ.get("CB_SCOPE", "comunidad")
    for c in ("usuarios", "cuarentena", "bitacora_carga"):
        total = list(cl.query(f"SELECT COUNT(*) AS total FROM `{b}`.`{sc}`.`{c}`"))[0]["total"]
        print(f"Couchbase - {c}:", total)
    q = (f"SELECT COUNT(*) AS con_geo FROM `{b}`.`{sc}`.`usuarios` "
         "WHERE consentimiento_datos = 'no' AND ubicacion IS NOT MISSING")
    print("Usuarios sin consentimiento que conservan objeto ubicacion:", list(cl.query(q))[0]["con_geo"])


# --------------------------------------------------------------------------
# Principal
# --------------------------------------------------------------------------
def main():
    cargar_dotenv()
    ap = argparse.ArgumentParser(description="ETL COMMUNITY 360 - Fase 2")
    ap.add_argument("--usuarios", default="datos/comunidad_virtual_usuarios.csv")
    ap.add_argument("--interacciones", default="datos/comunidad_virtual_1000_interacciones.csv")
    ap.add_argument("--salida", default="salida")
    ap.add_argument("--cargar", choices=["ninguno", "cassandra", "couchbase", "ambos"], default="ninguno")
    ap.add_argument("--verificar", choices=["ninguno", "cassandra", "couchbase", "ambos"], default="ninguno")
    a = ap.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s", datefmt="%H:%M:%S")

    if a.cargar == "ninguno" and a.verificar != "ninguno":
        if a.verificar in ("cassandra", "ambos"):
            verificar_cassandra()
        if a.verificar in ("couchbase", "ambos"):
            verificar_couchbase()
        return

    u, i = leer_csv(a.usuarios), leer_csv(a.interacciones)
    aud = {"entrada": {"usuarios": len(u), "interacciones": len(i)}}
    log.info("Leídos: %d usuarios, %d interacciones", len(u), len(i))

    u, i = etapa1_duplicados(u, i, aud)
    u, i = etapa2_nulos(u, i, aud)
    u, i = etapa3_estandarizar(u, i, aud)
    u, i, rech_u, rech_i = etapa4_tipos(u, i, aud)
    i, huerfanas = etapa5_referencias(u, i, aud)
    u, i, huerfanas = etapa6_privacidad(u, i, huerfanas, aud)
    aud["salida"] = {"usuarios": len(u), "interacciones": len(i)}
    etapa7_exportar(u, i, {"usuarios_invalidos": rech_u, "interacciones_invalidas": rech_i,
                           "interacciones_huerfanas": huerfanas}, a.salida, aud)

    if a.cargar in ("cassandra", "ambos"):
        cargar_cassandra(i)
    if a.cargar in ("couchbase", "ambos"):
        cargar_couchbase(u, huerfanas, aud, a.usuarios, a.interacciones)
    if a.verificar in ("cassandra", "ambos"):
        verificar_cassandra()
    if a.verificar in ("couchbase", "ambos"):
        verificar_couchbase()
    log.info("Proceso terminado. Usuarios: %d | Interacciones: %d", len(u), len(i))


if __name__ == "__main__":
    main()
