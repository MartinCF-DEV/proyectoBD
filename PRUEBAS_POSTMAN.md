# Pruebas de API REST y WebSockets en Postman · Community 360

Antes de enviar las pruebas, ten abierto el dashboard en `http://localhost:4200/dashboard` (en la sección **Tiempo real** o **Movimientos**) para ver cómo se actualizan los indicadores por WebSocket sin recargar la página.

---

## Paso 1. Obtener el Token JWT (`POST /api/auth/login`)

* **Método**: `POST`
* **URL**: `http://localhost:5000/api/auth/login`
* **Headers**: `Content-Type: application/json`
* **Body (`raw` -> `JSON`)**:

```json
{
  "correo": "lizandro.reyes",
  "password": "lizandr0_reye$"
}
```

> **Configurar el Token en Postman**:
> 1. Copia el valor de `"token"` que devuelve la respuesta `200 OK`.
> 2. En las siguientes peticiones ve a la pestaña **Authorization** -> **Auth Type**: `Bearer Token` -> pega el token en el campo **Token**.

---

## Paso 2. Crear una Interacción (`POST /api/interacciones`)

### Opción A: Interacción con ID específico (`I0010500`) para probar y luego borrar
* **Método**: `POST`
* **URL**: `http://localhost:5000/api/interacciones`
* **Authorization**: `Bearer Token`
* **Body (`raw` -> `JSON`)**:

```json
{
  "id_interaccion": "I0010500",
  "id_usuario": "U000002",
  "canal": "Telegram",
  "tipo": "mensaje",
  "tema": "videojuegos",
  "texto": "Prueba de insercion en vivo desde Postman",
  "sentimiento": 0.85,
  "dispositivo": "Escritorio",
  "reacciones_recibidas": 5,
  "etiquetas": ["juegos", "evento"]
}
```

* **Respuesta esperada (`201 Created`)**:
  * Valida que `U000002` existe en Couchbase Capella y toma su estado (`Jalisco`) porque tiene `consentimiento_datos = "si"`.
  * Inserta la interacción en las 5 tablas de DataStax Astra DB (`interaccion_por_id`, `interaccion_por_usuario`, `interaccion_por_tema`, `interaccion_por_estado` e `interaccion_por_etiqueta`).
  * Actualiza el dashboard en tiempo real por WebSocket (`N` sube `+1`, sube `videojuegos` y la celda `videojuegos × TG` del mapa de calor).

---

### Opción B: Interacción de tipo `reaccion` con usuario sin consentimiento (`U000004`)
* **Método**: `POST`
* **URL**: `http://localhost:5000/api/interacciones`
* **Authorization**: `Bearer Token`
* **Body (`raw` -> `JSON`)**:

```json
{
  "id_interaccion": "I0010501",
  "id_usuario": "U000004",
  "canal": "Discord",
  "tipo": "reaccion",
  "tema": "arte_comisiones",
  "texto": null,
  "sentimiento": 0.90,
  "dispositivo": "Android",
  "reacciones_recibidas": 2,
  "etiquetas": ["arte"]
}
```

* **Respuesta esperada (`201 Created`)**:
  * Como `U000004` tiene `consentimiento_datos = "no"`, guarda `"estado": null` por privacidad y sube el porcentaje de **`% reacciones`** en tiempo real.

---

## Paso 3. Modificar la Interacción (`PUT /api/interacciones/{id}`)

* **Método**: `PUT`
* **URL**: `http://localhost:5000/api/interacciones/I0010500`
* **Authorization**: `Bearer Token`
* **Body (`raw` -> `JSON`)**:

```json
{
  "canal": "Discord",
  "tipo": "comentario",
  "tema": "musica",
  "texto": "Interaccion modificada desde Postman cambiando tema y canal",
  "sentimiento": 0.95,
  "dispositivo": "Web",
  "reacciones_recibidas": 12,
  "etiquetas": ["musica", "humor"]
}
```

* **Respuesta esperada (`200 OK`)**:
  * El total `N` se mantiene igual, disminuye `videojuegos (-1)` y aumenta `musica (+1)` en las gráficas y en el mapa de calor en tiempo real.

---

## Paso 4. Eliminar la Interacción (`DELETE /api/interacciones/{id}`)

### Eliminar la interacción de la Opción A (`I0010500`)
* **Método**: `DELETE`
* **URL**: `http://localhost:5000/api/interacciones/I0010500`
* **Authorization**: `Bearer Token`
* **Body**: `none` *(no lleva JSON en el Body)*

* **Respuesta esperada (`200 OK`)**:
```json
{
  "id_interaccion": "I0010500",
  "mensaje": "Interacción eliminada de las 5 tablas de Cassandra."
}
```
*(Si vuelves a presionar **Send** en el mismo `DELETE`, devolverá **`404 Not Found`** porque ya fue eliminada).*

### Eliminar la interacción de la Opción B (`I0010501`)
* **Método**: `DELETE`
* **URL**: `http://localhost:5000/api/interacciones/I0010501`
* **Authorization**: `Bearer Token`
* **Body**: `none`

---

## Paso 5. Pruebas Rápidas de Validación (`401`, `404`, `400`)

1. **Sin Token (`401 Unauthorized`)**:
   * `GET http://localhost:5000/api/interacciones` con **Auth Type: `No Auth`**.
2. **Usuario inexistente en Couchbase (`404 Not Found`)**:
   * `POST http://localhost:5000/api/interacciones` con `"id_usuario": "U009999"`.
3. **Datos inválidos (`400 Bad Request`)**:
   * `POST http://localhost:5000/api/interacciones` con `"sentimiento": 2.5` o `"texto": ""` cuando `"tipo": "mensaje"`.
