#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Puente CQL con OpenSSL para sincronizar operaciones POST, PUT y DELETE
desde el backend .NET 10 hacia las 5 tablas de DataStax Astra DB (keyspace community360).
"""
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import etl_comunidad_virtual as etl


def parse_ts(ts_str):
    try:
        return datetime.fromisoformat(ts_str.replace("Z", "+00:00"))
    except Exception:
        return datetime.now(timezone.utc)


def insertar(sesion, d):
    ts = parse_ts(d["fecha_hora"])
    etiquetas_set = set(d.get("etiquetas") or [])

    ps_id = sesion.prepare("""
        INSERT INTO interaccion_por_id (
            id_interaccion, id_usuario, fecha_hora, canal, tipo, texto, tema,
            estado, sentimiento, idioma, dispositivo, longitud_texto,
            reacciones_recibidas, etiquetas
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """)
    sesion.execute(ps_id, (
        d["id_interaccion"], d["id_usuario"], ts, d["canal"], d["tipo"],
        d.get("texto"), d["tema"], d.get("estado"), float(d["sentimiento"]),
        d.get("idioma", "es"), d.get("dispositivo", "Web"),
        d.get("longitud_texto"), int(d.get("reacciones_recibidas", 0)),
        etiquetas_set if etiquetas_set else None
    ))

    ps_usr = sesion.prepare("""
        INSERT INTO interaccion_por_usuario (
            id_usuario, fecha_hora, id_interaccion, tema, canal, tipo, sentimiento
        ) VALUES (?, ?, ?, ?, ?, ?, ?)
    """)
    sesion.execute(ps_usr, (
        d["id_usuario"], ts, d["id_interaccion"], d["tema"],
        d["canal"], d["tipo"], float(d["sentimiento"])
    ))

    ps_tema = sesion.prepare("""
        INSERT INTO interaccion_por_tema (
            tema, canal, fecha_hora, id_interaccion, tipo, sentimiento, reacciones_recibidas
        ) VALUES (?, ?, ?, ?, ?, ?, ?)
    """)
    sesion.execute(ps_tema, (
        d["tema"], d["canal"], ts, d["id_interaccion"],
        d["tipo"], float(d["sentimiento"]), int(d.get("reacciones_recibidas", 0))
    ))

    if d.get("estado"):
        ps_est = sesion.prepare("""
            INSERT INTO interaccion_por_estado (
                estado, fecha_hora, id_interaccion, tema, sentimiento
            ) VALUES (?, ?, ?, ?, ?)
        """)
        sesion.execute(ps_est, (
            d["estado"], ts, d["id_interaccion"], d["tema"], float(d["sentimiento"])
        ))

    if etiquetas_set:
        ps_etiq = sesion.prepare("""
            INSERT INTO interaccion_por_etiqueta (
                etiqueta, fecha_hora, id_interaccion, tema, canal, sentimiento
            ) VALUES (?, ?, ?, ?, ?, ?)
        """)
        for et in sorted(etiquetas_set):
            sesion.execute(ps_etiq, (
                et, ts, d["id_interaccion"], d["tema"], d["canal"], float(d["sentimiento"])
            ))


def eliminar(sesion, d):
    ts = parse_ts(d["fecha_hora"])
    etiquetas_set = set(d.get("etiquetas") or [])

    ps_id = sesion.prepare("DELETE FROM interaccion_por_id WHERE id_interaccion = ?")
    sesion.execute(ps_id, (d["id_interaccion"],))

    ps_usr = sesion.prepare("DELETE FROM interaccion_por_usuario WHERE id_usuario = ? AND fecha_hora = ? AND id_interaccion = ?")
    sesion.execute(ps_usr, (d["id_usuario"], ts, d["id_interaccion"]))

    ps_tema = sesion.prepare("DELETE FROM interaccion_por_tema WHERE tema = ? AND canal = ? AND fecha_hora = ? AND id_interaccion = ?")
    sesion.execute(ps_tema, (d["tema"], d["canal"], ts, d["id_interaccion"]))

    if d.get("estado"):
        ps_est = sesion.prepare("DELETE FROM interaccion_por_estado WHERE estado = ? AND fecha_hora = ? AND id_interaccion = ?")
        sesion.execute(ps_est, (d["estado"], ts, d["id_interaccion"]))

    if etiquetas_set:
        ps_etiq = sesion.prepare("DELETE FROM interaccion_por_etiqueta WHERE etiqueta = ? AND fecha_hora = ? AND id_interaccion = ?")
        for et in sorted(etiquetas_set):
            sesion.execute(ps_etiq, (et, ts, d["id_interaccion"]))


def main():
    etl.cargar_dotenv(str(Path(__file__).resolve().parent.parent / ".env"))
    if len(sys.argv) < 2:
        sys.exit(1)

    accion = sys.argv[1]
    raw = sys.stdin.read() if len(sys.argv) < 3 else sys.argv[2]
    payload = json.loads(raw)
    sesion = etl._conectar_cassandra()
    try:
        if accion == "upsert":
            insertar(sesion, payload)
        elif accion == "update":
            eliminar(sesion, payload["anterior"])
            insertar(sesion, payload["actualizada"])
        elif accion == "delete":
            eliminar(sesion, payload)
    finally:
        sesion.shutdown()


if __name__ == "__main__":
    main()
