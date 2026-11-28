# Documentación Técnica del Proyecto — Community 360 (Fases 3 a 6)

---

## 2. Descripción General del Sistema y API

### 2.1 Integración y Análisis de la Comunidad Virtual
**Community 360** es una plataforma analítica híbrida en la nube diseñada para monitorear, auditar y consultar en tiempo real el comportamiento de una comunidad virtual digital conformada por **1,068 perfiles de usuarios** y sus flujos de interacción multicanal (**Telegram, Discord, Instagram, TikTok, Facebook y Twitter/X**).

El sistema unifica una arquitectura de **persistencia políglota en la nube**:
1. **Couchbase Capella (Base de datos documental JSON — `community360.comunidad`)**: Almacena entidades con estructura jerárquica, atributos anidados (como el subdocumento `ubicacion`) y metadatos de gobierno de datos: colecciones `usuarios` (1,068 documentos), `cuarentena` (793 interacciones rechazadas por integridad referencial), `bitacora_carga` (registro de auditoría ETL) y `cuentas` (credenciales administrativas cifradas con BCrypt).
2. **DataStax Astra DB / Apache Cassandra (Base de datos columnar distribuida — keyspace `community360`)**: Almacena el flujo de eventos e interacciones válidas (**208 registros iniciales**) aplicando el patrón de modelado **Query-Driven Design (una tabla por patrón de consulta)** a través de 5 tablas desnormalizadas sincronizadas: `interaccion_por_id`, `interaccion_por_usuario`, `interaccion_por_tema`, `interaccion_por_estado` e `interaccion_por_etiqueta`.

---

### 2.2 Explicación de los 4 Módulos Principales del Sistema

#### 1. Módulo de Autenticación y Control de Acceso (Login — Fase 3)
* **Cómo se implementó:**
  * El usuario ingresa su identificador (**nombre de usuario** como `lizandro.reyes` o **correo institucional** `lizandro.reyes@utmetropolitana.edu.mx`) y su contraseña en el componente `LoginComponent` de Angular.
  * El backend en **.NET 10 (`AuthController.cs`)** recibe la petición en `POST /api/auth/login` y consulta en vivo la colección `community360.comunidad.cuentas` en **Couchbase Capella** (tanto por clave primaria KV como mediante consulta SQL++ `WHERE LOWER(c.correo) = $id OR LOWER(c.usuario) = $id`).
  * Verifica que la cuenta tenga `"activa": true` y `"rol": "admin"`.
  * Valida la contraseña en texto plano contra el hash criptográfico **`password_hash`** almacenado en Couchbase utilizando **`BCrypt.Net-Next` (`BCrypt.Verify`)** con factor de costo 12 (`$2a$12$...`). Ninguna contraseña en texto plano se almacena en la base de datos ni en el código fuente.
  * Si la validación es exitosa, `JwtTokenService` firma un **JSON Web Token (JWT)** con algoritmo **HMAC-SHA256**, incluyendo los claims `sub`, `email`, `nombre`, `rol="admin"` y un identificador único de sesión `jti` (con vigencia de 120 minutos).
  * En el frontend, un guard funcional **`authGuard` (`CanActivateFn`)** protege la ruta `/dashboard`, mientras que el interceptor HTTP **`authInterceptor`** adjunta automáticamente la cabecera `Authorization: Bearer <token>` a todas las peticiones REST.
  * Al cerrar sesión (`POST /api/auth/logout`), el servicio **`TokenRevocationService`** registra el `jti` del token en una lista negra en memoria del servidor (validada en el evento `OnTokenValidated` de `JwtBearerEvents`), invalidando el JWT de forma inmediata en el backend.

#### 2. Módulo de Consulta Histórica vía API REST (Fase 4)
* **Cómo se implementó:**
  * Expone endpoints HTTP protegidos por `[Authorize(Roles = "admin")]` que permiten consultar el padrón de usuarios en Couchbase Capella y filtrar interacciones históricas en Cassandra por **usuario (`id_usuario`)**, **tema**, **canal**, **estado** y **etiqueta**, además de realizar operaciones transaccionales completas (`POST`, `PUT`, `DELETE`) desde clientes externos como Postman.
  * Cada operación de escritura valida dominios permitidos (`ValidadorInteracciones.cs`), verifica la **integridad referencial cruzada** (confirmando que `id_usuario` exista previamente en la colección `usuarios` de Couchbase Capella antes de insertar en Cassandra) y replica la escritura o eliminación en las **5 tablas de consulta de DataStax Astra DB**.

#### 3. Módulo de Tiempo Real con WebSockets (Fase 5)
* **Cómo se implementó:**
  * El backend en .NET 10 habilita el middleware nativo `app.UseWebSockets()` y expone el canal **`/ws`** (`wss://community360-1nii.onrender.com/ws`), administrado por el servicio singleton **`WebSocketHubService`**.
  * Cumpliendo la restricción arquitectónica de la Fase 5, **el dashboard web no contiene formularios ni botones para crear, editar o eliminar interacciones**. Todas las mutaciones (`POST`, `PUT`, `DELETE`) se ejecutan exclusivamente desde **Postman**.
  * En el instante en que `InteraccionesController` procesa un `POST`, `PUT` o `DELETE` válido, recalcula las métricas agregadas del sistema e invoca `_wsHub.BroadcastEventoAsync(...)`.
  * El servidor empuja un paquete JSON en tiempo real a todos los navegadores conectados con el método HTTP, código de respuesta, hora, traza de petición/respuesta HTTP, descripción del efecto (`N: 208 → 209`) y el objeto `dashboard` actualizado. El componente Angular actualiza los KPIs, gráfica de dona, barras, mapa de calor y bitácora de movimientos **sin recargar la página**.

#### 4. Módulo de Calidad de Datos y Privacidad Geográfica (Fase 2, 4 y 6)
* **Qué se hizo exactamente para la privacidad geográfica:**
  * **En el pipeline ETL y Couchbase Capella (1,068 usuarios):** Se verificó el campo `consentimiento_datos` de cada perfil. Para los **897 usuarios con consentimiento (`"si"`)**, se conserva el subdocumento JSON `ubicacion` (`pais`, `estado`, `ciudad`, `latitud`, `longitud`, `zona_horaria`). Para los **171 usuarios sin consentimiento (`"no"`)**, se suprimieron por completo los 6 atributos geográficos (**684 valores geográficos eliminados** en perfiles de usuario y **1,938 valores geográficos eliminados** en registros de cuarentena), asignando `"ubicacion": null` en Couchbase Capella para impedir cualquier rastreo de coordenadas o ciudad.
  * **En la API REST en tiempo real (`POST /api/interacciones` y `PUT /api/interacciones/{id}`):** Cuando se registra o actualiza una interacción, el backend consulta en Couchbase Capella el perfil del `id_usuario`. Si `usuario.ConsentimientoDatos == "si"`, hereda `estado = usuario.Ubicacion?.Estado` y la indexa en la tabla `interaccion_por_estado` de Cassandra; pero **si `usuario.ConsentimientoDatos == "no"`, el backend fuerza automáticamente `estado = null`** y omite su inserción en `interaccion_por_estado`, garantizando privacidad geográfica por diseño (*Privacy by Design*).
  * **En el Dashboard (`Calidad y privacidad`):** Se presenta el desglose de usuarios con y sin consentimiento (`897` vs `171`), el conteo de valores geográficos suprimidos, la tarjeta legible de un perfil anonimizado real (`U000004`, alias `AshCh'a30`, sin coordenadas ni ubicación), el registro de cargas y los `793` registros detenidos en pendientes de revisión (cuarentena).

---

### 2.3 Desglose Explícito de Todos los Endpoints de la API

El backend cuenta con **10 endpoints REST** distribuidos en 4 controladores (`AuthController`, `DashboardController`, `InteraccionesController` y `UsuariosController`) más **1 endpoint WebSocket** de transmisión en vivo:

| # | Método HTTP | Ruta / Endpoint | Autenticación | ¿Qué hace exactamente en el Backend? | ¿Qué parte del Frontend / Sistema lo consume? |
| :-: | :---: | :--- | :---: | :--- | :--- |
| **1** | `POST` | `/api/auth/login` | Público (`[AllowAnonymous]`) | Busca la cuenta por usuario o correo en Couchbase Capella (`cuentas`), verifica que esté activa, valida la contraseña con `BCrypt.Verify` y emite el token JWT firmado (200 OK / 401 Unauthorized). | **Pantalla de Login (`LoginComponent`)**: Formulario de inicio de sesión al presionar *"Iniciar sesión"*. |
| **2** | `GET` | `/api/auth/me` | JWT Bearer (`admin`) | Extrae el claim de correo/identidad del token JWT, verifica contra Couchbase Capella que la cuenta siga activa y devuelve los datos del administrador (`correo`, `nombre`, `rol`). | **Inicialización del Dashboard (`DashboardComponent.ngOnInit`)**: `authService.verifySession()` al cargar o refrescar `/dashboard`. |
| **3** | `POST` | `/api/auth/logout` | JWT Bearer (`admin`) | Extrae el identificador único `jti` y la fecha de expiración del token JWT actual y lo inserta en `TokenRevocationService` para invalidar su uso futuro. | **Barra Lateral del Dashboard**: Botón *"Cerrar sesión"* en la parte inferior del menú izquierdo. |
| **4** | `GET` | `/api/dashboard/resumen` | JWT Bearer (`admin`) | Calcula y devuelve todas las métricas agregadas en vivo: totales de usuarios (1,068), interacciones válidas (208), cuarentena (793), porcentaje de reacciones, tema líder, distribuciones por estatus/tema/tipo/canal/etiqueta, matriz del mapa de calor `Tema × Canal`, serie de tendencia diaria, muestra de cuarentena, perfil sin consentimiento y registro de cargas. | **4 Secciones del Dashboard**: *Resumen general*, *Tiempo real* (carga inicial), *Consulta histórica* (gráfica de tendencia diaria) y *Calidad y privacidad*. |
| **5** | `GET` | `/api/interacciones` | JWT Bearer (`admin`) | Consulta interacciones históricas en Cassandra permitiendo filtros combinables por query string: `?id_usuario=`, `&tema=`, `&canal=`, `&estado=`, `&etiqueta=` y `&limite=` (1 a 500), ordenadas por fecha descendente. | **Sección Histórico (`DashboardComponent.buscarInteracciones`)**: Tabla histórica interactiva, barra de 4 filtros y gráfica *"Por canal"*. |
| **6** | `GET` | `/api/interacciones/{id}` | JWT Bearer (`admin`) | Busca una interacción específica por su clave primaria (`id_interaccion`, ej. `I0000018`) en la tabla `interaccion_por_id` de Cassandra (200 OK / 404 Not Found). | **Pruebas de verificación en Postman** (consulta puntual posterior a un `POST` o `PUT`). |
| **7** | `POST` | `/api/interacciones` | JWT Bearer (`admin`) | Valida dominios (canal, tipo, tema, sentimiento `[-1, 1]`, texto obligatorio salvo en `reaccion`), verifica que `id_usuario` exista en Couchbase Capella (404 si es huérfano), aplica la regla de privacidad geográfica (`estado = null` si `consentimiento_datos == "no"`), inserta en las 5 tablas de Cassandra y emite evento por WebSocket (201 Created). | **Exclusivo desde Postman (Fase 5)**. El dashboard recibe su efecto en vivo vía WebSocket en *Tiempo real* y *Movimientos*. |
| **8** | `PUT` | `/api/interacciones/{id}` | JWT Bearer (`admin`) | Verifica que la interacción exista (404 si no existe), valida los nuevos campos, elimina las entradas obsoletas en las tablas secundarias de Cassandra si cambió el tema/estado/etiquetas, sobrescribe el registro actualizado en las 5 tablas de Cassandra y notifica por WebSocket (200 OK). | **Exclusivo desde Postman (Fase 5)**. El dashboard refleja el cambio de tema/canal/tipo al instante vía WebSocket. |
| **9** | `DELETE` | `/api/interacciones/{id}` | JWT Bearer (`admin`) | Verifica la existencia del `id_interaccion` (404 si no existe), elimina el registro de las 5 tablas de Cassandra (`por_id`, `por_usuario`, `por_tema`, `por_estado` y `por_etiqueta`) y transmite el decremento en vivo por WebSocket (200 OK). | **Exclusivo desde Postman (Fase 5)**. El dashboard actualiza `N` y registra la evidencia HTTP en *Movimientos*. |
| **10** | `GET` | `/api/usuarios` | JWT Bearer (`admin`) | Consulta el padrón de perfiles de usuarios en Couchbase Capella con soporte de filtrado por `?estatus=` (`activo`, `en_riesgo`, `inactivo`), `&busqueda=` (ID, alias o estado) y `&limite=`. | **Consulta administrativa REST / Postman** para auditoría del padrón de usuarios. |
| **11** | `GET` | `/api/usuarios/{id}` | JWT Bearer (`admin`) | Obtiene el perfil completo de un usuario en Couchbase Capella (`U000001`..`U001068`) respetando su anonimización geográfica, y cruza sus interacciones asociadas en Cassandra. | **Consulta administrativa REST / Postman** para expediente individual de usuario. |
| **WS** | `WS / WSS` | `/ws` | Canal WebSocket | Mantiene la conexión bidireccional persistente (`WebSocketHubService`) y empuja los eventos `POST`, `PUT` y `DELETE` con el resumen recalculado del dashboard en tiempo real. | **Secciones *Tiempo real* y *Movimientos*** del Dashboard (`conectarWebSocket()`). |

---

## 3. Evolución del Stack Tecnológico

### 3.1 Tabla del Stack Tecnológico Final y Comparación con la Fase 1

| Capa / Componente | Propuesta Original (Fase 1) | Stack Final Implementado (Fases 3–6) | Dónde está Desplegado / Alojado | ¿Hubo Cambio? / Razón Técnica y Operativa |
| :--- | :--- | :--- | :--- | :--- |
| **Base de Datos Documental (NoSQL)** | **Couchbase Capella** (`bucket: community360`, `scope: comunidad`) | **Couchbase Capella** (`community360.comunidad`: colecciones `usuarios`, `cuarentena`, `bitacora_carga`, `cuentas`) | **Couchbase Capella Cloud** (AWS `us-east` / Clúster administrado DBaaS) | **Sin cambio de motor.** Se amplió en la Fase 3 agregando la colección `cuentas` para almacenar los usuarios administradores con hashes BCrypt. |
| **Base de Datos Columnar (NoSQL)** | **DataStax Astra DB (Apache Cassandra)** (`keyspace: community360`) | **DataStax Astra DB (Apache Cassandra Serverless)** (5 tablas: `interaccion_por_id`, `por_usuario`, `por_tema`, `por_estado`, `por_etiqueta`) | **DataStax Astra DB Cloud** (Región `us-east-2` con Secure Connect Bundle mTLS) | **Sin cambio.** Se mantuvo el diseño original de 5 tablas orientadas a consultas (*Query-Driven Modeling*) para lecturas de baja latencia. |
| **Backend / API REST** | **.NET (C#) / ASP.NET Core Web API** | **ASP.NET Core Web API en .NET 10 (C#)** con `CouchbaseNetClient 3.6.5`, `CassandraCSharpDriver 3.22.0`, `JwtBearer 10.0.0` y `BCrypt.Net-Next 4.0.3` | **Render.com** (Contenedor Docker Linux unificado en `https://community360-1nii.onrender.com`) | **Evolución a .NET 10.** Se utilizó la versión más reciente del runtime (.NET 10) sirviendo tanto la API REST (`/api/*`), el hub WebSocket (`/ws`) como los archivos estáticos compilados de Angular (`wwwroot`). |
| **Frontend / SPA** | **Angular** | **Angular 19 (Standalone Components + Signals + Control Flow `@if/@for`)** con TypeScript 5.7 | **Render.com** (Compilado en etapa multi-stage de Docker y servido desde el mismo origen HTTPS del backend) | **Evolución a Angular 19 Signals.** Se adoptó el modelo reactivo nativo de `signal()` y `computed()` de Angular 19 para actualizar las gráficas al instante cuando llega un mensaje WebSocket, sin sobrecarga de librerías externas de estado. |
| **Comunicación en Tiempo Real (WebSockets)** | **WebSockets** (planteado inicialmente con SignalR o WebSocket estándar) | **WebSockets Nativos RFC 6455** (`Microsoft.AspNetCore.WebSockets` en `/ws` + API nativa `window.WebSocket` del navegador) | **Render.com** (`wss://community360-1nii.onrender.com/ws`) | **Ajuste técnico a WebSocket nativo puro.** En lugar de acoplar el protocolo propietario de SignalR, se implementó un Hub WebSocket nativo (`WebSocketHubService.cs`), lo que redujo el peso del bundle de Angular, eliminó problemas de negociación CORS/proxies en la nube y permitió enviar el payload JSON limpio en una sola trama. |
| **Visualización y Gráficas** | **Librería de gráficas externas** (Chart.js / ng2-charts o Apache ECharts) | **Motor propio de gráficas vectoriales SVG reactivas con Angular Signals y CSS Grid** | Renderizado directo en el DOM del cliente (navegador) | **Cambio técnico importante.** Se reemplazaron librerías pesadas basadas en `<canvas>` (como Chart.js) por **gráficas SVG nativas reactivas** (`<svg viewBox>`, `<circle stroke-dasharray>` para la dona, `<polyline>`/`<polygon>` para la tendencia temporal y CSS Grid dinámico con interpolación RGBA para el mapa de calor `Tema × Canal`). **Razón:** Cero dependencias externas que fallen al compilar, 100% responsivo en cualquier pantalla y transiciones fluidas instantáneas al recibir eventos WebSocket. |
| **Plataforma de Despliegue Cloud** | **Vercel (Frontend) + Servicio Cloud separado para Backend** | **Despliegue Unificado con Docker Multi-Stage en Render.com** (`Dockerfile` con `node:22-alpine` + `dotnet/sdk:10.0` + `dotnet/aspnet:10.0`) | **Render.com** (`https://community360-1nii.onrender.com`) | **Cambio operativo clave.** Vercel está diseñado para frontends estáticos o funciones serverless en Node.js, pero **no soporta servidores persistentes en C# (.NET 10) ni conexiones WebSocket de larga duración con túneles mTLS de Cassandra**. Al empaquetar Angular + .NET 10 en un solo contenedor Docker en Render, el frontend, la API REST y el WebSocket (`wss://`) comparten el mismo dominio HTTPS, eliminando bloqueos de CORS, Mixed Content y desconexiones de sockets. |

---

## 4. Análisis de Base de Datos y Escalabilidad (Enfoque Principal)

### 4.1 Tipo de Motor y Arquitectura de Modelado en la Nube
El sistema implementa una **arquitectura NoSQL Híbrida (Persistencia Políglota)** que combina dos motores especializados en la nube según la naturaleza del dato:

1. **Motor Documental Distribuido — Couchbase Capella (`bucket: community360`, `scope: comunidad`)**:
   * **Por qué se usa:** Los perfiles de usuario tienen atributos semi-estructurados y jerárquicos (como el objeto anidado `ubicacion` que existe cuando `consentimiento_datos == "si"` y desaparece como `null` cuando `consentimiento_datos == "no"`), además de requerir búsquedas puntuales Key-Value (`O(1)`) por `id_usuario` y consultas declarativas en **SQL++ (N1QL)** para autenticación en `cuentas`.
   * **Colecciones implementadas (4):**
     * `usuarios` (**1,068 documentos**): Clave primaria documental `U000001`..`U001068`. Incluye `alias`, `fecha_ingreso`, `rango_edad`, `especie_fursona`, `interes_principal`, `plataforma_principal`, `es_artista`, `mensajes_30d`, `reacciones_30d`, `eventos_asistidos`, `comisiones_compradas`, `sentimiento_promedio`, `ultima_actividad`, `estatus`, `consentimiento_datos` y el subdocumento condicional `ubicacion`.
     * `cuentas` (**2 documentos**): Cuentas administrativas con `correo`, `usuario`, `nombre`, `rol`, `activa`, `creada_en` y `password_hash` (BCrypt costo 12). Cuenta con índice primario SQL++ (`CREATE PRIMARY INDEX IF NOT EXISTS ON community360.comunidad.cuentas`).
     * `cuarentena` (**793 documentos**): Interacciones rechazadas durante el proceso ETL por violar la integridad referencial (`id_usuario_inexistente_en_usuarios`), conservadas para auditoría con sus campos geográficos depurados.
     * `bitacora_carga` (**4 documentos**): Metadatos de auditoría de las corridas ETL (`ETL-USUARIOS`, `ETL-INTERACCIONES`, conteos de leídos, aceptados, transformados y rechazados).

2. **Motor Columnar de Ancho de Columna Distribuido — DataStax Astra DB / Apache Cassandra (`keyspace: community360`)**:
   * **Por qué se usa:** Las interacciones sociales son series de eventos de alta velocidad de escritura e inmutables en su mayoría, donde las consultas analíticas siempre filtran por dimensiones conocidas (por ID, por usuario, por tema, por estado o por etiqueta).
   * **Esquema de las 5 tablas desnormalizadas en CQL:**
     ```sql
     -- 1. Búsqueda puntual por identificador único de interacción
     CREATE TABLE community360.interaccion_por_id (
         id_interaccion text PRIMARY KEY,
         id_usuario text, fecha_hora timestamp, canal text, tipo text,
         texto text, tema text, estado text, sentimiento float,
         reacciones_recibidas int, etiquetas set<text>
     );

     -- 2. Historial de interacciones de un usuario ordenado cronológicamente
     CREATE TABLE community360.interaccion_por_usuario (
         id_usuario text,
         fecha_hora timestamp,
         id_interaccion text,
         canal text, tipo text, texto text, tema text, estado text,
         sentimiento float, reacciones_recibidas int, etiquetas set<text>,
         PRIMARY KEY ((id_usuario), fecha_hora, id_interaccion)
     ) WITH CLUSTERING ORDER BY (fecha_hora DESC, id_interaccion ASC);

     -- 3. Análisis y filtrado por tema de conversación
     CREATE TABLE community360.interaccion_por_tema (
         tema text,
         fecha_hora timestamp,
         id_interaccion text,
         id_usuario text, canal text, tipo text, texto text,
         estado text, sentimiento float, reacciones_recibidas int,
         PRIMARY KEY ((tema), fecha_hora, id_interaccion)
     ) WITH CLUSTERING ORDER BY (fecha_hora DESC, id_interaccion ASC);

     -- 4. Distribución geográfica por estado (solo usuarios con consentimiento_datos = 'si')
     CREATE TABLE community360.interaccion_por_estado (
         estado text,
         fecha_hora timestamp,
         id_interaccion text,
         id_usuario text, canal text, tipo text, tema text, sentimiento float,
         PRIMARY KEY ((estado), fecha_hora, id_interaccion)
     ) WITH CLUSTERING ORDER BY (fecha_hora DESC, id_interaccion ASC);

     -- 5. Índice invertido desnormalizado por etiqueta (1 fila por cada etiqueta de la interacción)
     CREATE TABLE community360.interaccion_por_etiqueta (
         etiqueta text,
         fecha_hora timestamp,
         id_interaccion text,
         id_usuario text, canal text, tipo text, tema text, sentimiento float,
         PRIMARY KEY ((etiqueta), fecha_hora, id_interaccion)
     ) WITH CLUSTERING ORDER BY (fecha_hora DESC, id_interaccion ASC);
     ```

### 4.2 Diagrama de Arquitectura y Modelo de Datos Híbrido

```mermaid
erDiagram
    COUCHBASE_USUARIOS {
        string id_usuario PK "U000001..U001068"
        string alias
        string fecha_ingreso
        string rango_edad
        string especie_fursona
        boolean es_artista
        int mensajes_30d
        int reacciones_30d
        string estatus "activo | en_riesgo | inactivo"
        string consentimiento_datos "si | no"
        object ubicacion "NULL si consentimiento = no"
    }

    COUCHBASE_CUENTAS {
        string correo PK "admin / lizandro.reyes"
        string usuario "lizandro.reyes"
        string password_hash "BCrypt $2a$12$..."
        string nombre
        string rol "admin"
        boolean activa
    }

    COUCHBASE_CUARENTENA {
        string id_interaccion PK "793 registros rechazados"
        string id_usuario "Huérfano (no existe en usuarios)"
        string canal
        string tema
        string motivo_rechazo
    }

    CASSANDRA_INTERACCION_POR_ID {
        text id_interaccion PK
        text id_usuario FK
        timestamp fecha_hora
        text canal
        text tipo
        text tema
        text estado "NULL si usuario sin consentimiento"
        float sentimiento
        set_text etiquetas
    }

    CASSANDRA_INTERACCION_POR_USUARIO {
        text id_usuario PK "Partition Key"
        timestamp fecha_hora CK "Clustering DESC"
        text id_interaccion CK
    }

    CASSANDRA_INTERACCION_POR_TEMA {
        text tema PK "Partition Key"
        timestamp fecha_hora CK "Clustering DESC"
        text id_interaccion CK
    }

    CASSANDRA_INTERACCION_POR_ESTADO {
        text estado PK "Partition Key (182 con consentimiento)"
        timestamp fecha_hora CK "Clustering DESC"
        text id_interaccion CK
    }

    CASSANDRA_INTERACCION_POR_ETIQUETA {
        text etiqueta PK "Partition Key (319 filas)"
        timestamp fecha_hora CK "Clustering DESC"
        text id_interaccion CK
    }

    COUCHBASE_USUARIOS ||--o{ CASSANDRA_INTERACCION_POR_ID : "Integridad referencial validada en API (.NET 10)"
    COUCHBASE_USUARIOS ||--o{ CASSANDRA_INTERACCION_POR_USUARIO : "1 usuario : N interacciones"
    CASSANDRA_INTERACCION_POR_ID ||--|| CASSANDRA_INTERACCION_POR_TEMA : "Fan-out escritura x5"
    CASSANDRA_INTERACCION_POR_ID ||--o| CASSANDRA_INTERACCION_POR_ESTADO : "Solo si consentimiento = si"
    CASSANDRA_INTERACCION_POR_ID ||--o{ CASSANDRA_INTERACCION_POR_ETIQUETA : "1 fila por etiqueta"
```

---

### 4.3 Análisis de Escalabilidad: ¿Qué pasaría al pasar de ~1,000 a Cientos de Miles de Registros en Tiempo Real?

Actualmente el volumen del dataset (~1,068 usuarios y ~208 interacciones válidas) cabe holgadamente en la memoria RAM del contenedor de Render (`512 MB`) y las agregaciones en memoria de `CloudDatabaseService.cs` toman menos de **2 milisegundos**. Sin embargo, si la comunidad escalara a **500,000+ interacciones y miles de eventos por minuto en tiempo real**, aparecerían **5 cuellos de botella técnicos reales** en la implementación actual:

1. **Particiones Calientes (*Hot Partitions* / *Wide Rows*) en Cassandra por Baja Cardinalidad:**
   * **Causa real en nuestro esquema:** En `interaccion_por_tema`, la clave de partición (`PRIMARY KEY ((tema), fecha_hora, id_interaccion)`) tiene solo **7 valores posibles** (`videojuegos`, `musica`, `fursuits`, etc.). En `interaccion_por_etiqueta` solo hay **7 etiquetas**.
   * **Impacto a gran escala:** Si ingresan 700,000 interacciones, cada partición de `tema` acumularía ~100,000 filas dentro de un solo nodo físico de Cassandra, generando *hotspots* de escritura, particiones gigantes (*wide partitions* > 100 MB) y degradación en la compactación de SSTables.
2. **Amplificación de Escritura (*Write Fan-Out*) No Atómica en las 5 Tablas de Cassandra:**
   * **Causa real en nuestro código (`CloudDatabaseService.PersistirInteraccionEnCassandraAsync`):** Cada vez que llega un `POST /api/interacciones`, el backend ejecuta `1` escritura en `por_id` + `1` en `por_usuario` + `1` en `por_tema` + `1` en `por_estado` + `N` en `por_etiqueta` (entre **5 y 8 sentencias `INSERT` individuales** mediante `Task.WhenAll`). Además, en `PUT` y `DELETE`, ejecuta `DELETE` individuales en cada tabla antes de reinsertar.
   * **Impacto a gran escala:** Con miles de peticiones concurrentes por segundo, si una de las 6 escrituras falla por latencia de red hacia Astra DB, las tablas secundarias pueden quedar desincronizadas respecto a `interaccion_por_id`.
3. **Recálculo Completo en Memoria del Resumen Analítico (`GetDashboardResumenAsync`) y Espejo en Disco (`GuardarInteraccionesEnCsv`):**
   * **Causa real en nuestro código:** En `CloudDatabaseService.cs`, el diccionario en memoria `_interaccionesStore` (`ConcurrentDictionary`) mantiene todas las interacciones cargadas en RAM para ejecutar `GroupBy` con LINQ en cada petición, y cada `POST/PUT/DELETE` invoca `GuardarInteraccionesEnCsv()` que reescribe el archivo CSV de respaldo completo en disco.
   * **Impacto a gran escala:** Con 500,000 interacciones, reescribir un CSV en cada `POST` saturaría el I/O de disco del contenedor en milisegundos, y el diccionario en RAM agotaría los `512 MB` del plan gratuito de Render (`OutOfMemoryException`).
4. **Saturación de Red en el Hub WebSocket (`WebSocketHubService.BroadcastEventoAsync`):**
   * **Causa real en nuestro código:** Cada operación `POST/PUT/DELETE` envía inmediatamente un mensaje WebSocket con el objeto `dashboard` completo a todos los clientes conectados.
   * **Impacto a gran escala:** Si entraran 500 interacciones por segundo, el servidor enviaría 500 paquetes JSON completos por segundo a cada navegador, saturando el hilo de renderizado de Angular y el ancho de banda del socket.
5. **Lectura Cruzada Síncrona hacia Couchbase en cada `POST` de Interacción:**
   * **Causa real:** Para verificar si el `id_usuario` existe y si tiene `consentimiento_datos == "si"`, el backend consulta el perfil del usuario antes de insertar en Cassandra. A cientos de miles de usuarios que ya no quepan en caché local, hacer un `GetAsync` por red a Couchbase Capella en cada interacción añadiría latencia de red cruzada entre nubes.

---

### 4.4 Propuestas Técnicas de Mejora para Escalar a Cientos de Miles de Registros

Para escalar **Community 360** a cientos de miles o millones de registros manteniendo tiempo real, se proponen **5 cambios técnicos concretos sobre nuestra arquitectura actual**:

1. **Time-Bucketing (Particionamiento Compuesto por Ventana de Tiempo) en Cassandra:**
   * Modificar la clave de partición de las tablas de baja cardinalidad (`interaccion_por_tema`, `interaccion_por_estado` e `interaccion_por_etiqueta`) agregando el mes o semana (`anio_mes text`, ej. `'2026-11'`):
     ```sql
     PRIMARY KEY ((tema, anio_mes), fecha_hora, id_interaccion)
     ```
   * **Beneficio:** Divide las particiones masivas en fragmentos mensuales acotados y distribuye la carga uniformemente entre todos los nodos del clúster de DataStax Astra DB.
2. **Uso de `LOGGED BATCH` en Cassandra para Atomicidad en el Fan-Out de las 5 Tablas:**
   * Agrupar los `INSERT` y `DELETE` de una misma interacción (`por_id`, `por_usuario`, `por_tema`, `por_estado`, `por_etiqueta`) dentro de un `BatchStatement` (`LOGGED BATCH`) del driver `CassandraCSharpDriver`.
   * **Beneficio:** Garantiza que las 5 tablas se actualicen de manera atómica (o se escriben todas o ninguna) en un solo viaje de red (*single network round-trip*) hacia el nodo coordinador de Cassandra.
3. **Contadores Incrementales y Caché Distribuida (Redis / Contadores Atómicos) en lugar de Recálculo `GroupBy` Total:**
   * Eliminar la escritura síncrona a CSV (`GuardarInteraccionesEnCsv`) en producción y reemplazar el `GroupBy` completo sobre `_interaccionesStore` por **contadores atómicos en memoria o Redis** (o tablas `COUNTER` en Cassandra / vistas de agregación en Couchbase SQL++). Así, un `POST` solo incrementa `conteo[tema]++` y `conteo[canal]++` en `O(1)` sin recorrer los 500,000 registros.
4. **Throttling / Micro-Batching en el Hub WebSocket (`System.Threading.Channels`):**
   * Desacoplar la recepción de eventos en `WebSocketHubService` mediante una cola en memoria (`Channel<Evento>`) y emitir actualizaciones al dashboard en ventanas agrupadas cada **250 ms – 500 ms** (*debouncing/throttling*) en lugar de un broadcast por cada petición individual.
5. **Caché LRU con TTL para Perfiles de Consentimiento de Couchbase:**
   * Mantener en el backend un `IMemoryCache` o Redis con política LRU (*Least Recently Used*) que almacene únicamente la tupla ligera `(id_usuario → consentimiento_datos, estado)` con TTL de 15 minutos, evitando cargar el documento completo de `usuarios` en memoria y reduciendo a cero las lecturas de red hacia Couchbase Capella durante ráfagas de inserción.

---

## 5. Enlaces y Credenciales de Evaluación

### 5.1 Enlaces Públicos del Proyecto Desplegado
* **URL Pública del Sistema en Producción (Login y Dashboard):**  
  [https://community360-1nii.onrender.com](https://community360-1nii.onrender.com)
* **Acceso Directo al Dashboard Analítico (protegido por Guard JWT):**  
  [https://community360-1nii.onrender.com/dashboard](https://community360-1nii.onrender.com/dashboard)
* **URL Base de la API REST (para pruebas en Postman):**  
  `https://community360-1nii.onrender.com/api`
* **Canal Interno de WebSocket (conectado automáticamente por el Dashboard):**  
  `wss://community360-1nii.onrender.com/ws` *(No es un enlace para abrir en la barra del navegador; el Dashboard se conecta a este canal internamente de forma automática).*

---

### 5.2 Credenciales del Usuario Docente Habilitado en Couchbase Capella
El usuario de evaluación del profesor se encuentra **creado, habilitado (`"activa": true`, `"rol": "admin"`) y verificado** directamente en la colección **`community360.comunidad.cuentas`** de **Couchbase Capella** con su contraseña cifrada mediante **BCrypt (`$2a$12$...`)**:

| Campo | Valor Habilitado |
| :--- | :--- |
| **Usuario (Login directo)** | `lizandro.reyes` |
| **Correo Institucional (Login alternativo)** | `lizandro.reyes@utmetropolitana.edu.mx` |
| **Contraseña** | `lizandr0_reye$` |
| **Rol en Couchbase Capella** | `admin` |
| **Colección en la Nube** | `community360.comunidad.cuentas` |

> **Nota sobre el inicio de sesión:** El endpoint `POST /api/auth/login` y la pantalla de acceso aceptan indistintamente tanto el usuario **`lizandro.reyes`** como el correo completo **`lizandro.reyes@utmetropolitana.edu.mx`** junto con la contraseña **`lizandr0_reye$`**.
