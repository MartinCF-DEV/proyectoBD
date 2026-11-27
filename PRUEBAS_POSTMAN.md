# Pruebas de API REST y WebSockets en Postman · Community 360 (Render Cloud)

* **URL del Dashboard en línea**: `https://community360-1nii.onrender.com/dashboard`
* **URL Base de la API en línea**: `https://community360-1nii.onrender.com/api`
* **WebSocket en línea**: `wss://community360-1nii.onrender.com/ws`

Antes de enviar las pruebas desde Postman, abre en tu navegador **`https://community360-1nii.onrender.com/dashboard`** (en la sección **Tiempo real** o **Movimientos**) para ver cómo se actualizan las gráficas, el mapa de calor y la bitácora en vivo por WebSocket sin recargar la página.

---

## Paso 1. Obtener el Token JWT (`POST /api/auth/login`)

* **Método**: `POST`
* **URL**: `https://community360-1nii.onrender.com/api/auth/login`
* **Headers**: `Content-Type: application/json`
* **Body (`raw` -> `JSON`)**:

```json
{
  "correo": "lizandro.reyes",
  "password": "lizandr0_reye$"
}
```
*(También puedes usar `"correo": "lizandro.reyes@utmetropolitana.edu.mx"` o la cuenta `"correo": "admin@community360.mx"` con `"password": "Community360!2026"`).*

> **Configurar el Token en Postman**:
> 1. Copia el valor de `"token"` que devuelve la respuesta `200 OK`.
> 2. En las siguientes peticiones ve a la pestaña **Authorization** -> **Auth Type**: `Bearer Token` -> pega el token en el campo **Token**.

---

## Paso 2. Crear una Interacción (`POST /api/interacciones`)

### Opción A: Interacción con ID específico (`I0010500`) para probar y luego editar/borrar
* **Método**: `POST`
* **URL**: `https://community360-1nii.onrender.com/api/interacciones`
* **Authorization**: `Bearer Token`
* **Body (`raw` -> `JSON`)**:

```json
{
  "id_interaccion": "I0010500",
  "id_usuario": "U000002",
  "canal": "Telegram",
  "tipo": "mensaje",
  "tema": "videojuegos",
  "texto": "Prueba de insercion en vivo desde Postman hacia Render",
  "sentimiento": 0.85,
  "dispositivo": "Escritorio",
  "reacciones_recibidas": 5,
  "etiquetas": ["juegos", "evento"]
}
```

* **Respuesta esperada (`201 Created`)**:
  * Valida que `U000002` existe en Couchbase Capella y toma su estado (`Jalisco`) porque tiene `consentimiento_datos = "si"`.
  * Actualiza el dashboard en `https://community360-1nii.onrender.com/dashboard` en tiempo real por WebSocket (`Interacciones vigentes` sube `+1`, sube `videojuegos` y la celda `videojuegos × TG` del mapa de calor).

---

### Opción B: Interacción de tipo `reaccion` con usuario sin consentimiento (`U000004`)
* **Método**: `POST`
* **URL**: `https://community360-1nii.onrender.com/api/interacciones`
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
* **URL**: `https://community360-1nii.onrender.com/api/interacciones/I0010500`
* **Authorization**: `Bearer Token`
* **Body (`raw` -> `JSON`)**:

```json
{
  "canal": "Discord",
  "tipo": "comentario",
  "tema": "musica",
  "texto": "Interaccion modificada desde Postman cambiando tema a musica y canal a Discord",
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
* **URL**: `https://community360-1nii.onrender.com/api/interacciones/I0010500`
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
* **URL**: `https://community360-1nii.onrender.com/api/interacciones/I0010501`
* **Authorization**: `Bearer Token`
* **Body**: `none`

---

## Paso 5. Consultas Históricas (`GET`) y Cierre de Sesión (`POST`)

Con **Authorization: `Bearer Token`**:

1. **Listar interacciones con filtros**:
   * `GET` -> `https://community360-1nii.onrender.com/api/interacciones?id_usuario=U000002&tema=videojuegos`
2. **Consultar una interacción por ID**:
   * `GET` -> `https://community360-1nii.onrender.com/api/interacciones/I0001825`
3. **Consultar perfil de un usuario y sus interacciones**:
   * `GET` -> `https://community360-1nii.onrender.com/api/usuarios/U000002` *(con consentimiento y objeto `ubicacion`)*
   * `GET` -> `https://community360-1nii.onrender.com/api/usuarios/U000004` *(sin consentimiento y sin objeto `ubicacion`)*
4. **Consultar resumen general del dashboard**:
   * `GET` -> `https://community360-1nii.onrender.com/api/dashboard/resumen`
5. **Cerrar sesión (revocar el token JWT actual)**:
   * `POST` -> `https://community360-1nii.onrender.com/api/auth/logout`

---

## Paso 6. Pruebas de Validación y Códigos de Error HTTP (`401`, `404`, `400`)

1. **Sin Token (`401 Unauthorized`)**:
   * `GET` -> `https://community360-1nii.onrender.com/api/interacciones` con **Auth Type: `No Auth`**.
2. **Usuario inexistente en Couchbase (`404 Not Found`)**:
   * `POST` -> `https://community360-1nii.onrender.com/api/interacciones` con `"id_usuario": "U009999"`:
   ```json
   {
     "id_usuario": "U009999",
     "canal": "Telegram",
     "tipo": "mensaje",
     "tema": "videojuegos",
     "texto": "Usuario inexistente en el padron de Couchbase",
     "sentimiento": 0.5,
     "dispositivo": "Web",
     "reacciones_recibidas": 0,
     "etiquetas": ["juegos"]
   }
   ```
3. **Datos fuera de rango o texto vacío (`400 Bad Request`)**:
   * `POST` -> `https://community360-1nii.onrender.com/api/interacciones`:
   ```json
   {
     "id_usuario": "U000002",
     "canal": "CanalInvalido",
     "tipo": "mensaje",
     "tema": "videojuegos",
     "texto": "",
     "sentimiento": 2.5,
     "dispositivo": "Web",
     "reacciones_recibidas": -3,
     "etiquetas": ["etiqueta_invalida"]
   }
   ```
