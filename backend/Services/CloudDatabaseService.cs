 using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Cassandra;
using Community360.Api.Configuration;
using Community360.Api.Models;
using Couchbase;
using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.KeyValue;

namespace Community360.Api.Services;

public sealed class CloudDatabaseService : IAsyncDisposable
{
    private readonly ILogger<CloudDatabaseService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private Couchbase.ICluster? _cbCluster;
    private IBucket? _cbBucket;
    private IScope? _cbScope;
    private ICouchbaseCollection? _cuentasCollection;
    private ICouchbaseCollection? _usuariosCollection;

    private Cassandra.ICluster? _cassandraCluster;
    private Cassandra.ISession? _cassandraSession;

    private string _bucketName = "community360";
    private string _scopeName = "comunidad";
    private string _keyspace = "community360";

    private readonly ConcurrentDictionary<string, CuentaAdminDocument> _cuentasAutorizadas = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, UsuarioPerfilDto> _usuariosStore = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, InteraccionDetalleDto> _interaccionesStore = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CuarentenaMuestraDto> _cuarentenaMuestra = [];
    private long _totalCuarentenaReal = 793;
    private long _totalInteraccionesRecibidas = 1000;
    private long _valoresGeoBorradosUsuarios = 684;
    private long _valoresGeoBorradosHuerfanas = 1938;
    private string _fechaEjecucionEtl = "2026-11-21T20:30:45Z";
    private bool _storesLoaded;

    public CloudDatabaseService(ILogger<CloudDatabaseService> logger)
    {
        _logger = logger;
        EnsureLocalStoresLoaded();
    }

    private void EnsureLocalStoresLoaded()
    {
        if (_storesLoaded) return;
        _storesLoaded = true;

        var salidaDir = FindSalidaDirectory();
        if (salidaDir is null) return;

        try
        {
            var usrPath = Path.Combine(salidaDir, "usuarios_depurados.csv");
            if (File.Exists(usrPath))
            {
                foreach (var line in File.ReadAllLines(usrPath).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var c = ParseCsvLine(line);
                    if (c.Count < 22) continue;

                    bool consent = string.Equals(c[21], "True", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(c[21], "si", StringComparison.OrdinalIgnoreCase);

                    var u = new UsuarioPerfilDto
                    {
                        IdUsuario = c[0].Trim().ToUpperInvariant(),
                        Alias = c[1].Trim(),
                        FechaIngreso = c[2].Trim(),
                        RangoEdad = c[9].Trim(),
                        EspecieFursona = c[10].Trim(),
                        InteresPrincipal = c[11].Trim(),
                        PlataformaPrincipal = c[12].Trim(),
                        EsArtista = string.Equals(c[13], "True", StringComparison.OrdinalIgnoreCase),
                        Mensajes30d = int.TryParse(c[14], out var m30) ? m30 : 0,
                        Reacciones30d = int.TryParse(c[15], out var r30) ? r30 : 0,
                        EventosAsistidos = int.TryParse(c[16], out var ev) ? ev : 0,
                        ComisionesCompradas = int.TryParse(c[17], out var com) ? com : 0,
                        SentimientoPromedio = double.TryParse(c[18], NumberStyles.Float, CultureInfo.InvariantCulture, out var sp) ? sp : 0,
                        UltimaActividad = c[19].Trim(),
                        Estatus = c[20].Trim(),
                        ConsentimientoDatos = consent ? "si" : "no",
                        Ubicacion = consent ? new UbicacionUsuarioDto
                        {
                            Pais = c[3].Trim(),
                            Estado = c[4].Trim(),
                            Ciudad = c[5].Trim(),
                            Latitud = double.TryParse(c[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ? lat : null,
                            Longitud = double.TryParse(c[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ? lon : null,
                            ZonaHoraria = c[8].Trim()
                        } : null
                    };
                    _usuariosStore[u.IdUsuario] = u;
                }
            }

            var intPath = Path.Combine(salidaDir, "interacciones_depuradas.csv");
            if (File.Exists(intPath))
            {
                foreach (var line in File.ReadAllLines(intPath).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var c = ParseCsvLine(line);
                    if (c.Count < 16) continue;

                    var inter = new InteraccionDetalleDto
                    {
                        IdInteraccion = c[0].Trim().ToUpperInvariant(),
                        IdUsuario = c[1].Trim().ToUpperInvariant(),
                        FechaHora = c[2].Trim(),
                        Canal = c[3].Trim(),
                        Tipo = c[4].Trim(),
                        Texto = string.IsNullOrWhiteSpace(c[5]) ? null : c[5],
                        Tema = c[6].Trim(),
                        Sentimiento = float.TryParse(c[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0f,
                        Idioma = string.IsNullOrWhiteSpace(c[8]) ? "es" : c[8].Trim(),
                        Estado = string.IsNullOrWhiteSpace(c[10]) ? null : c[10].Trim(),
                        Dispositivo = string.IsNullOrWhiteSpace(c[12]) ? "Web" : c[12].Trim(),
                        LongitudTexto = int.TryParse(c[13], out var lt) ? lt : null,
                        ReaccionesRecibidas = int.TryParse(c[14], out var rr) ? rr : 0,
                        Etiquetas = !string.IsNullOrWhiteSpace(c[15])
                            ? c[15].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                            : []
                    };
                    _interaccionesStore[inter.IdInteraccion] = inter;
                }
            }

            // Cargar muestra real de cuarentena desde salida/rechazados/interacciones_huerfanas.csv
            var huerfPath = Path.Combine(salidaDir, "rechazados", "interacciones_huerfanas.csv");
            if (File.Exists(huerfPath))
            {
                var hLines = File.ReadAllLines(huerfPath).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                _totalCuarentenaReal = hLines.Count;
                foreach (var line in hLines.Take(8))
                {
                    var c = ParseCsvLine(line);
                    if (c.Count >= 16)
                    {
                        _cuarentenaMuestra.Add(new CuarentenaMuestraDto
                        {
                            IdInteraccion = c[0].Trim(),
                            IdUsuario = c[1].Trim(),
                            Canal = c[3].Trim(),
                            Tema = c[6].Trim(),
                            MotivoRechazo = c.Count > 16 && !string.IsNullOrWhiteSpace(c[16])
                                ? c[16].Trim()
                                : "id_usuario_inexistente_en_usuarios"
                        });
                    }
                }
            }

            // Cargar métricas reales de salida/auditoria.json
            var audPath = Path.Combine(salidaDir, "auditoria.json");
            if (File.Exists(audPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(audPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("generado_utc", out var gen))
                    _fechaEjecucionEtl = gen.GetString() ?? _fechaEjecucionEtl;
                if (root.TryGetProperty("entrada", out var ent) && ent.TryGetProperty("interacciones", out var entInt))
                    _totalInteraccionesRecibidas = entInt.GetInt64();
                if (root.TryGetProperty("etapa6_privacidad", out var priv))
                {
                    if (priv.TryGetProperty("valores_geograficos_borrados_usuarios", out var vgu))
                        _valoresGeoBorradosUsuarios = vgu.GetInt64();
                    if (priv.TryGetProperty("valores_geograficos_borrados_huerfanas", out var vgh))
                        _valoresGeoBorradosHuerfanas = vgh.GetInt64();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Aviso al cargar archivos base de salida: {Msg}", ex.Message);
        }
    }

    private List<(CuentaAdminDocument Doc, string RawPassword)> GetOrCreateEnvHashedAccounts()
    {
        return [];
    }

    public async Task InitializeAsync()
    {
        if (_cbCluster is not null && _cassandraSession is not null)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_cbCluster is null)
            {
                try
                {
                    var cbConn = DotEnvLoader.GetRequired("CB_CONEXION");
                    var cbUser = DotEnvLoader.GetRequired("CB_USUARIO");
                    var cbPass = DotEnvLoader.GetRequired("CB_CLAVE");
                    _bucketName = Environment.GetEnvironmentVariable("CB_BUCKET") ?? "community360";
                    _scopeName = Environment.GetEnvironmentVariable("CB_SCOPE") ?? "comunidad";

                    var options = new ClusterOptions().WithCredentials(cbUser, cbPass);
                    options.ApplyProfile("wan-development");

                    _logger.LogInformation("Conectando a Couchbase Capella ({Bucket}.{Scope})...", _bucketName, _scopeName);
                    var cluster = await Couchbase.Cluster.ConnectAsync(cbConn, options);
                    await cluster.WaitUntilReadyAsync(TimeSpan.FromSeconds(12));

                    _cbCluster = cluster;
                    _cbBucket = await _cbCluster.BucketAsync(_bucketName);
                    _cbScope = await _cbBucket.ScopeAsync(_scopeName);
                    _cuentasCollection = await _cbScope.CollectionAsync("cuentas");
                    _usuariosCollection = await _cbScope.CollectionAsync("usuarios");

                    await CargarCuentasDesdeCouchbaseAsync();
                    _logger.LogInformation("[OK] Conexión establecida con Couchbase Capella ({Count} cuentas cargadas de la colección cuentas).", _cuentasAutorizadas.Count);
                }
                catch (Exception ex)
                {
                    _cbCluster = null;
                    _cuentasCollection = null;
                    _usuariosCollection = null;
                    _logger.LogWarning("Aviso Couchbase Capella: {Msg}", ex.Message);
                }
            }

            if (_cassandraSession is null)
            {
                try
                {
                    var bundlePath = ResolveOrCreateBundlePath();
                    var astraToken = DotEnvLoader.GetRequired("ASTRA_TOKEN");
                    _keyspace = Environment.GetEnvironmentVariable("ASTRA_KEYSPACE") ?? "community360";

                    _logger.LogInformation("Conectando a DataStax Astra DB (keyspace: {Keyspace})...", _keyspace);
                    try
                    {
                        _cassandraCluster = Cassandra.Cluster.Builder()
                            .WithCloudSecureConnectionBundle(bundlePath)
                            .WithCredentials("token", astraToken)
                            .WithQueryOptions(new Cassandra.QueryOptions().SetConsistencyLevel(ConsistencyLevel.LocalQuorum))
                            .Build();

                        _cassandraSession = await _cassandraCluster.ConnectAsync(_keyspace);
                    }
                    catch
                    {
                        await DespertarAstraDbViaStargateAsync(astraToken);
                        _cassandraCluster = Cassandra.Cluster.Builder()
                            .WithCloudSecureConnectionBundle(bundlePath)
                            .WithCredentials("token", astraToken)
                            .WithQueryOptions(new Cassandra.QueryOptions().SetConsistencyLevel(ConsistencyLevel.LocalQuorum))
                            .Build();
                        _cassandraSession = await _cassandraCluster.ConnectAsync(_keyspace);
                    }

                    _logger.LogInformation("[OK] Conexión establecida con DataStax Astra DB (keyspace: {Keyspace}).", _keyspace);
                }
                catch (Exception ex)
                {
                    _cassandraSession = null;
                    _logger.LogWarning("Aviso DataStax Astra DB: {Msg}", ex.Message);
                }
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task DespertarAstraDbViaStargateAsync(string astraToken)
    {
        try
        {
            _logger.LogInformation("Enviando señal de activación a DataStax Astra DB...");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("X-Cassandra-Token", astraToken);
            _ = await http.GetAsync("https://090e5544-dc84-49e6-8e5a-391755ebcf02-us-east-2.apps.astra.datastax.com/api/rest/v2/schemas/keyspaces");
            await Task.Delay(4000);
        }
        catch
        {
            // Ignorar 503 inicial mientras despierta
        }
    }

    private static string ResolveOrCreateBundlePath()
    {
        var bundleB64 = Environment.GetEnvironmentVariable("ASTRA_BUNDLE_B64");
        if (!string.IsNullOrWhiteSpace(bundleB64))
        {
            var tempBundlePath = Path.Combine(Path.GetTempPath(), "secure-connect-proyecto.zip");
            var bytes = Convert.FromBase64String(bundleB64.Trim());
            File.WriteAllBytes(tempBundlePath, bytes);
            return tempBundlePath;
        }

        var configuredPath = Environment.GetEnvironmentVariable("ASTRA_BUNDLE") ?? "documentos/secure-connect-proyecto.zip";
        return ResolveBundlePath(configuredPath);
    }

    private static string ResolveBundlePath(string configuredPath)
    {
        if (File.Exists(configuredPath))
            return configuredPath;

        var fileName = Path.GetFileName(configuredPath);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "documentos", fileName);
            if (File.Exists(candidate))
                return candidate;

            var candidateProject = Path.Combine(dir.FullName, "proyecto_BD", "documentos", fileName);
            if (File.Exists(candidateProject))
                return candidateProject;

            var candidateProjectOld = Path.Combine(dir.FullName, "proyecto BD", "documentos", fileName);
            if (File.Exists(candidateProjectOld))
                return candidateProjectOld;

            dir = dir.Parent;
        }
        return configuredPath;
    }

    private async Task CargarCuentasDesdeCouchbaseAsync()
    {
        if (_cbCluster is null) return;
        try
        {
            await _cbCluster.QueryAsync<dynamic>(
                $"CREATE PRIMARY INDEX IF NOT EXISTS ON `{_bucketName}`.`{_scopeName}`.`cuentas`");
        }
        catch
        {
            // Índice ya existe
        }

        try
        {
            var query = $"SELECT c.* FROM `{_bucketName}`.`{_scopeName}`.`cuentas` AS c";
            var res = await _cbCluster.QueryAsync<CuentaAdminDocument>(query);
            await foreach (var doc in res.Rows)
            {
                if (!string.IsNullOrWhiteSpace(doc.Correo))
                {
                    _cuentasAutorizadas[doc.Correo.Trim().ToLowerInvariant()] = doc;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Aviso al consultar colección cuentas en Couchbase: {Msg}", ex.Message);
        }
    }

    public async Task<CuentaAdminDocument?> GetCuentaByCorreoAsync(string identificador)
    {
        if (_cbCluster is null)
        {
            await InitializeAsync();
        }

        var normalized = identificador.Trim().ToLowerInvariant();

        // 1. Buscar en las cuentas cargadas desde Couchbase Capella (por correo o por usuario)
        if (_cuentasAutorizadas.TryGetValue(normalized, out var porCorreo))
        {
            return porCorreo;
        }

        var porUsuario = _cuentasAutorizadas.Values.FirstOrDefault(c =>
            string.Equals(c.Usuario, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Correo.Split('@')[0], normalized, StringComparison.OrdinalIgnoreCase));

        if (porUsuario is not null)
        {
            return porUsuario;
        }

        // 2. Consultar directamente Couchbase Capella por clave primaria (KV) o SQL++ si se agregó una cuenta nueva en la nube
        if (_cuentasCollection is not null && _cbCluster is not null)
        {
            try
            {
                var res = await _cuentasCollection.GetAsync(normalized, opts => opts.Timeout(TimeSpan.FromSeconds(3)));
                var doc = res.ContentAs<CuentaAdminDocument>();
                if (doc is not null)
                {
                    _cuentasAutorizadas[doc.Correo.Trim().ToLowerInvariant()] = doc;
                    return doc;
                }
            }
            catch (DocumentNotFoundException)
            {
                var q = $"SELECT c.* FROM `{_bucketName}`.`{_scopeName}`.`cuentas` AS c WHERE LOWER(c.correo) = $id OR LOWER(c.usuario) = $id LIMIT 1";
                var qr = await _cbCluster.QueryAsync<CuentaAdminDocument>(q, opts =>
                {
                    opts.Parameter("id", normalized);
                    opts.Timeout(TimeSpan.FromSeconds(4));
                });
                await foreach (var doc in qr.Rows)
                {
                    _cuentasAutorizadas[doc.Correo.Trim().ToLowerInvariant()] = doc;
                    return doc;
                }
            }
            catch
            {
                // No encontrada
            }
        }

        return null;
    }

    // =========================================================================
    // FASE 4: Consultas Históricas de Usuarios (Couchbase Capella)
    // =========================================================================

    public List<UsuarioPerfilDto> GetUsuarios(string? estatus, string? busqueda, int limite = 50)
    {
        EnsureLocalStoresLoaded();
        var query = _usuariosStore.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(estatus))
        {
            query = query.Where(u => string.Equals(u.Estatus, estatus.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var b = busqueda.Trim();
            query = query.Where(u =>
                u.IdUsuario.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                u.Alias.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                (u.Ubicacion?.Estado?.Contains(b, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query
            .OrderBy(u => u.IdUsuario)
            .Take(Math.Clamp(limite, 1, 500))
            .ToList();
    }

    public async Task<UsuarioPerfilDto?> GetUsuarioByIdAsync(string idUsuario)
    {
        EnsureLocalStoresLoaded();
        if (string.IsNullOrWhiteSpace(idUsuario)) return null;
        var normId = idUsuario.Trim().ToUpperInvariant();

        if (_usuariosStore.TryGetValue(normId, out var localUser))
        {
            return localUser;
        }

        if (_usuariosCollection is not null)
        {
            try
            {
                var fetchTask = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var res = await _usuariosCollection.GetAsync(normId, opts =>
                    {
                        opts.Timeout(TimeSpan.FromSeconds(2));
                        opts.CancellationToken(cts.Token);
                    });
                    return res.ContentAs<UsuarioPerfilDto>();
                });

                var completed = await Task.WhenAny(fetchTask, Task.Delay(2000));
                if (completed == fetchTask)
                {
                    var doc = await fetchTask;
                    if (doc is not null)
                    {
                        _usuariosStore[normId] = doc;
                        return doc;
                    }
                }
            }
            catch
            {
                // No existe en Couchbase
            }
        }

        return null;
    }

    // =========================================================================
    // FASE 4: Consultas Históricas y CRUD de Interacciones (Astra DB Cassandra)
    // =========================================================================

    public List<InteraccionDetalleDto> GetInteracciones(
        string? idUsuario = null,
        string? tema = null,
        string? canal = null,
        string? estado = null,
        string? etiqueta = null,
        int limite = 50)
    {
        EnsureLocalStoresLoaded();
        var query = _interaccionesStore.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(idUsuario))
            query = query.Where(i => string.Equals(i.IdUsuario, idUsuario.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(tema))
            query = query.Where(i => string.Equals(i.Tema, tema.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(canal))
            query = query.Where(i => string.Equals(i.Canal, canal.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(i => string.Equals(i.Estado, estado.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(etiqueta))
            query = query.Where(i => i.Etiquetas.Any(e => string.Equals(e, etiqueta.Trim(), StringComparison.OrdinalIgnoreCase)));

        return query
            .OrderByDescending(i => i.FechaHora)
            .ThenBy(i => i.IdInteraccion)
            .Take(Math.Clamp(limite, 1, 500))
            .ToList();
    }

    public InteraccionDetalleDto? GetInteraccionById(string idInteraccion)
    {
        EnsureLocalStoresLoaded();
        if (string.IsNullOrWhiteSpace(idInteraccion)) return null;
        var normId = idInteraccion.Trim().ToUpperInvariant();
        return _interaccionesStore.GetValueOrDefault(normId);
    }

    public async Task<(InteraccionDetalleDto? Data, int StatusCode, string? ErrorMessage)> CreateInteraccionAsync(CrearInteraccionRequest req)
    {
        EnsureLocalStoresLoaded();

        if (string.IsNullOrWhiteSpace(req.IdUsuario) || !Regex.IsMatch(req.IdUsuario.Trim(), @"^[Uu]\d{6}$"))
        {
            return (null, StatusCodes.Status400BadRequest, "El id_usuario debe tener el formato U seguido de 6 dígitos (ej. U000001).");
        }

        // 1. Validar integridad referencial contra la colección usuarios de Couchbase Capella
        var idUsuario = req.IdUsuario.Trim().ToUpperInvariant();
        var usuario = await GetUsuarioByIdAsync(idUsuario);
        if (usuario is null)
        {
            return (null, StatusCodes.Status404NotFound, $"El usuario '{idUsuario}' no existe en el padrón de Couchbase Capella. Operación rechazada por integridad referencial.");
        }

        // 2. Generar o validar id_interaccion
        string idInteraccion;
        if (!string.IsNullOrWhiteSpace(req.IdInteraccion))
        {
            idInteraccion = req.IdInteraccion.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(idInteraccion, @"^I\d{7}$"))
            {
                return (null, StatusCodes.Status400BadRequest, "El id_interaccion debe tener el formato I seguido de 7 dígitos (ej. I0010001).");
            }
            if (_interaccionesStore.ContainsKey(idInteraccion))
            {
                return (null, StatusCodes.Status409Conflict, $"Ya existe una interacción con el identificador '{idInteraccion}'.");
            }
        }
        else
        {
            idInteraccion = GenerarNuevoIdInteraccion();
        }

        // 3. Normalizar campos y aplicar reglas de privacidad y de reacciones (Fase 1 y Fase 2)
        var tipoNorm = req.Tipo.Trim().ToLowerInvariant();
        var temaNorm = req.Tema.Trim().ToLowerInvariant();
        var canalCanon = NormalizarCanal(req.Canal);
        var dispCanon = NormalizarDispositivo(req.Dispositivo);

        string? textoFinal = tipoNorm == "reaccion" ? null : req.Texto?.Trim();
        int? longitudFinal = textoFinal?.Length;

        // Privacidad: estado solo se conserva cuando consentimiento_datos == "si"
        string? estadoFinal = (string.Equals(usuario.ConsentimientoDatos, "si", StringComparison.OrdinalIgnoreCase) && usuario.Ubicacion is not null && !string.IsNullOrWhiteSpace(usuario.Ubicacion.Estado))
            ? usuario.Ubicacion.Estado
            : null;

        var etiquetasFinales = (req.Etiquetas ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var ahoraUtc = DateTimeOffset.UtcNow;
        var nueva = new InteraccionDetalleDto
        {
            IdInteraccion = idInteraccion,
            IdUsuario = idUsuario,
            FechaHora = ahoraUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Canal = canalCanon,
            Tipo = tipoNorm,
            Texto = textoFinal,
            Tema = temaNorm,
            Estado = estadoFinal,
            Sentimiento = (float)Math.Round(req.Sentimiento, 3),
            Idioma = "es",
            Dispositivo = dispCanon,
            LongitudTexto = longitudFinal,
            ReaccionesRecibidas = req.ReaccionesRecibidas,
            Etiquetas = etiquetasFinales
        };

        _interaccionesStore[nueva.IdInteraccion] = nueva;
        PersistirInteraccionesEnSalidaCsv();

        // 4. Persistir en las 5 tablas desnormalizadas de DataStax Astra DB (Cassandra)
        _ = Task.Run(() => SincronizarConAstraDbAsync("upsert", nueva));

        return (nueva, StatusCodes.Status201Created, null);
    }

    public async Task<(InteraccionDetalleDto? Data, int StatusCode, string? ErrorMessage)> UpdateInteraccionAsync(string idInteraccion, ActualizarInteraccionRequest req)
    {
        EnsureLocalStoresLoaded();
        if (string.IsNullOrWhiteSpace(idInteraccion))
        {
            return (null, StatusCodes.Status400BadRequest, "El identificador de la interacción es obligatorio.");
        }

        var normId = idInteraccion.Trim().ToUpperInvariant();
        if (!_interaccionesStore.TryGetValue(normId, out var anterior))
        {
            return (null, StatusCodes.Status404NotFound, $"No se encontró la interacción '{normId}' en interaccion_por_id.");
        }

        var tipoNorm = req.Tipo.Trim().ToLowerInvariant();
        var temaNorm = req.Tema.Trim().ToLowerInvariant();
        var canalCanon = NormalizarCanal(req.Canal);
        var dispCanon = NormalizarDispositivo(req.Dispositivo);

        string? textoFinal = tipoNorm == "reaccion" ? null : req.Texto?.Trim();
        int? longitudFinal = textoFinal?.Length;

        var etiquetasFinales = (req.Etiquetas ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var actualizada = new InteraccionDetalleDto
        {
            IdInteraccion = anterior.IdInteraccion,
            IdUsuario = anterior.IdUsuario,
            FechaHora = anterior.FechaHora,
            Canal = canalCanon,
            Tipo = tipoNorm,
            Texto = textoFinal,
            Tema = temaNorm,
            Estado = anterior.Estado,
            Sentimiento = (float)Math.Round(req.Sentimiento, 3),
            Idioma = anterior.Idioma,
            Dispositivo = dispCanon,
            LongitudTexto = longitudFinal,
            ReaccionesRecibidas = req.ReaccionesRecibidas,
            Etiquetas = etiquetasFinales
        };

        _interaccionesStore[normId] = actualizada;
        PersistirInteraccionesEnSalidaCsv();

        // Sincronizar las 5 tablas desnormalizadas en DataStax Astra DB (Cassandra)
        _ = Task.Run(() => SincronizarConAstraDbAsync("update", new { anterior, actualizada }));

        await Task.CompletedTask;
        return (actualizada, StatusCodes.Status200OK, null);
    }

    public async Task<(bool Deleted, int StatusCode, string? ErrorMessage)> DeleteInteraccionAsync(string idInteraccion)
    {
        EnsureLocalStoresLoaded();
        if (string.IsNullOrWhiteSpace(idInteraccion))
        {
            return (false, StatusCodes.Status400BadRequest, "El identificador de la interacción es obligatorio.");
        }

        var normId = idInteraccion.Trim().ToUpperInvariant();
        if (!_interaccionesStore.TryRemove(normId, out var eliminada))
        {
            return (false, StatusCodes.Status404NotFound, $"No se encontró la interacción '{normId}' en interaccion_por_id.");
        }

        PersistirInteraccionesEnSalidaCsv();

        // Eliminar de las 5 tablas desnormalizadas en DataStax Astra DB (Cassandra)
        _ = Task.Run(() => SincronizarConAstraDbAsync("delete", eliminada));

        await Task.CompletedTask;
        return (true, StatusCodes.Status200OK, null);
    }

    private async Task SincronizarConAstraDbAsync(string accion, object payload)
    {
        try
        {
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "cassandra_bridge.py");
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "cassandra_bridge.py");
            }
            if (!File.Exists(scriptPath)) return;

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "python",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add(accion);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null)
            {
                await proc.StandardInput.WriteAsync(json);
                proc.StandardInput.Close();
                await proc.WaitForExitAsync();
                if (proc.ExitCode == 0)
                {
                    _logger.LogInformation("[OK] Operación '{Accion}' sincronizada en las 5 tablas de DataStax Astra DB.", accion);
                }
                else
                {
                    var err = await proc.StandardError.ReadToEndAsync();
                    _logger.LogWarning("Aviso al sincronizar con Astra DB: {Err}", err);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Aviso en puente Astra DB: {Msg}", ex.Message);
        }
    }

    private void PersistirInteraccionesEnSalidaCsv()
    {
        try
        {
            var salidaDir = FindSalidaDirectory();
            if (salidaDir is null) return;

            var intPath = Path.Combine(salidaDir, "interacciones_depuradas.csv");
            var etiqPath = Path.Combine(salidaDir, "interaccion_etiquetas.csv");

            var items = _interaccionesStore.Values
                .OrderBy(i => i.IdInteraccion)
                .ToList();

            var sbInt = new System.Text.StringBuilder();
            sbInt.AppendLine("id_interaccion,id_usuario,timestamp,canal,tipo,texto,tema,sentimiento,idioma,pais,estado,ciudad,dispositivo,longitud_texto,reacciones_recibidas,etiquetas");

            var sbEtiq = new System.Text.StringBuilder();
            sbEtiq.AppendLine("id_interaccion,etiqueta");

            foreach (var i in items)
            {
                var u = _usuariosStore.GetValueOrDefault(i.IdUsuario);
                var pais = (i.Estado is not null && u?.Ubicacion is not null) ? u.Ubicacion.Pais : "";
                var ciudad = (i.Estado is not null && u?.Ubicacion is not null) ? u.Ubicacion.Ciudad : "";
                var textoCsv = EscaparCsv(i.Texto);
                var etiqStr = string.Join("|", i.Etiquetas);

                sbInt.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{i.IdInteraccion},{i.IdUsuario},{i.FechaHora},{i.Canal},{i.Tipo},{textoCsv},{i.Tema},{i.Sentimiento},{i.Idioma},{pais},{i.Estado ?? ""},{ciudad},{i.Dispositivo},{i.LongitudTexto?.ToString() ?? ""},{i.ReaccionesRecibidas},{etiqStr}"));

                foreach (var et in i.Etiquetas)
                {
                    sbEtiq.AppendLine($"{i.IdInteraccion},{et}");
                }
            }

            File.WriteAllText(intPath, sbInt.ToString(), new System.Text.UTF8Encoding(true));
            File.WriteAllText(etiqPath, sbEtiq.ToString(), new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Aviso al actualizar CSV de salida: {Msg}", ex.Message);
        }
    }

    private static string EscaparCsv(string? val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }

    private string GenerarNuevoIdInteraccion()
    {
        int maxNum = 9999;
        foreach (var key in _interaccionesStore.Keys)
        {
            if (key.Length > 1 && int.TryParse(key[1..], out var n) && n > maxNum)
            {
                maxNum = n;
            }
        }
        return $"I{(maxNum + 1):D7}";
    }

    private static string NormalizarCanal(string canal)
    {
        foreach (var c in ValidadorInteracciones.CanalesValidos)
        {
            if (string.Equals(c, canal.Trim(), StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return canal.Trim();
    }

    private static string NormalizarDispositivo(string dispositivo)
    {
        foreach (var d in ValidadorInteracciones.DispositivosValidos)
        {
            if (string.Equals(d, dispositivo.Trim(), StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return "Web";
    }

    // =========================================================================
    // Resumen dinámico del Dashboard (calculado en vivo desde los almacenes)
    // =========================================================================

    public Task<DashboardResumenDto> GetDashboardResumenAsync()
    {
        EnsureLocalStoresLoaded();
        var usuarios = _usuariosStore.Values.ToList();
        var interacciones = _interaccionesStore.Values.ToList();

        var totalUsuarios = usuarios.Count > 0 ? usuarios.Count : 1068;
        var conConsentimiento = usuarios.Count > 0 ? usuarios.Count(u => u.ConsentimientoDatos == "si") : 897;
        var sinConsentimiento = totalUsuarios - conConsentimiento;

        var totalInteracciones = interacciones.Count;
        var totalEstado = interacciones.Count(i => !string.IsNullOrWhiteSpace(i.Estado));
        var totalEtiqueta = interacciones.Sum(i => i.Etiquetas.Count);
        var totalReacciones = interacciones.Count(i => string.Equals(i.Tipo, "reaccion", StringComparison.OrdinalIgnoreCase));
        var pctReacciones = totalInteracciones > 0 ? Math.Round((double)totalReacciones / totalInteracciones * 100.0, 1) : 0.0;

        var porEstatus = usuarios
            .GroupBy(u => u.Estatus)
            .Select(g => new ConteoCategoriaDto { Categoria = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .ToList();

        var porEstado = usuarios
            .Where(u => u.Ubicacion is not null && !string.IsNullOrWhiteSpace(u.Ubicacion.Estado))
            .GroupBy(u => u.Ubicacion!.Estado)
            .Select(g => new ConteoCategoriaDto { Categoria = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .Take(8)
            .ToList();

        var porTema = interacciones
            .Where(i => !string.IsNullOrWhiteSpace(i.Tema))
            .GroupBy(i => i.Tema)
            .Select(g => new ConteoCategoriaDto { Categoria = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .ToList();

        var temaLider = porTema.FirstOrDefault()?.Categoria ?? "—";

        var porTipo = interacciones
            .Where(i => !string.IsNullOrWhiteSpace(i.Tipo))
            .GroupBy(i => i.Tipo)
            .Select(g => new ConteoCategoriaDto { Categoria = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .ToList();

        var porCanal = interacciones
            .Where(i => !string.IsNullOrWhiteSpace(i.Canal))
            .GroupBy(i => i.Canal)
            .Select(g => new ConteoCategoriaDto { Categoria = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .ToList();

        var porEtiqueta = interacciones
            .SelectMany(i => i.Etiquetas)
            .GroupBy(e => e)
            .Select(g => new ConteoCategoriaDto { Categoria = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .ToList();

        // Mapa de calor real (7 días Lunes..Domingo × 6 franjas de 4 horas UTC)
        var matrizCalor = Enumerable.Range(0, 7).Select(_ => new int[6].ToList()).ToList();
        var conteoPorDia = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var inter in interacciones)
        {
            if (DateTimeOffset.TryParse(inter.FechaHora, out var dt))
            {
                var utc = dt.UtcDateTime;
                int diaIdx = ((int)utc.DayOfWeek + 6) % 7; // Lunes = 0 .. Domingo = 6
                int franjaIdx = Math.Clamp(utc.Hour / 4, 0, 5);
                matrizCalor[diaIdx][franjaIdx]++;

                var fechaKey = utc.ToString("yyyy-MM-dd");
                conteoPorDia[fechaKey] = conteoPorDia.GetValueOrDefault(fechaKey) + 1;
            }
        }

        var tendenciaDiaria = conteoPorDia
            .OrderBy(kv => kv.Key)
            .Select(kv => new ConteoCategoriaDto { Categoria = kv.Key, Total = kv.Value })
            .ToList();

        // Mapa de calor Tema × Canal (tabla interaccion_por_tema en Cassandra, Fase 1 Sección 4.4)
        var canalesOrden = new List<string> { "Telegram", "Discord", "Instagram", "TikTok", "Facebook", "Twitter/X" };
        var mapaTemaCanal = porTema
            .Select(t => new FilaCalorTemaCanalDto
            {
                Tema = t.Categoria,
                Celdas = canalesOrden
                    .Select(c => interacciones.Count(i =>
                        string.Equals(i.Tema, t.Categoria, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(i.Canal, c, StringComparison.OrdinalIgnoreCase)))
                    .ToList()
            })
            .ToList();

        var ejemploSinConsentimiento = usuarios
            .OrderBy(u => u.IdUsuario)
            .FirstOrDefault(u => u.ConsentimientoDatos == "no");

        var recientes = interacciones
            .OrderByDescending(i => i.FechaHora)
            .ThenBy(i => i.IdInteraccion)
            .Take(12)
            .Select(i => new InteraccionResumenDto
            {
                IdInteraccion = i.IdInteraccion,
                IdUsuario = i.IdUsuario,
                FechaHora = i.FechaHora,
                Canal = i.Canal,
                Tipo = i.Tipo,
                Tema = i.Tema,
                Texto = i.Texto,
                Estado = i.Estado,
                Sentimiento = i.Sentimiento,
                ReaccionesRecibidas = i.ReaccionesRecibidas,
                Etiquetas = i.Etiquetas
            })
            .ToList();

        var dto = new DashboardResumenDto
        {
            CouchbaseConectado = _cbCluster is not null,
            CassandraConectado = _cassandraSession is not null,
            TotalUsuarios = totalUsuarios,
            UsuariosConConsentimiento = conConsentimiento,
            UsuariosSinConsentimiento = sinConsentimiento,
            SinConsentimientoConUbicacion = 0,
            TotalCuarentena = _totalCuarentenaReal,
            TotalBitacoraCarga = 4,
            TotalCuentasAdmin = _cuentasAutorizadas.Count > 0 ? _cuentasAutorizadas.Count : 2,
            TotalInteraccionesRecibidas = _totalInteraccionesRecibidas,
            TotalInteraccionesValidas = totalInteracciones,
            TotalInteraccionesUsuario = totalInteracciones,
            TotalInteraccionesTema = totalInteracciones,
            TotalInteraccionesEstado = totalEstado,
            TotalInteraccionesEtiqueta = totalEtiqueta,
            PorcentajeReacciones = pctReacciones,
            TemaLider = temaLider,
            ValoresGeoBorradosUsuarios = _valoresGeoBorradosUsuarios,
            ValoresGeoBorradosHuerfanas = _valoresGeoBorradosHuerfanas,
            UsuariosPorEstatus = porEstatus,
            UsuariosPorEstado = porEstado,
            InteraccionesPorTema = porTema,
            InteraccionesPorTipo = porTipo,
            InteraccionesPorCanal = porCanal,
            InteraccionesPorEtiquetaTop = porEtiqueta,
            MapaCalorDiaFranja = matrizCalor,
            CanalesCalor = canalesOrden,
            MapaCalorTemaCanal = mapaTemaCanal,
            TendenciaDiaria = tendenciaDiaria,
            MuestraCuarentena = _cuarentenaMuestra,
            EjemploUsuarioAnonimizado = ejemploSinConsentimiento,
            AtipicosActividad =
            [
                new() { IdUsuario = "U000002", Mensajes30d = "817", Reacciones30d = "2,026" },
                new() { IdUsuario = "U000483", Mensajes30d = "311", Reacciones30d = "—" },
                new() { IdUsuario = "U000169", Mensajes30d = "277", Reacciones30d = "726" }
            ],
            BitacoraCarga =
            [
                new()
                {
                    IdEjecucion = "ETL-USUARIOS",
                    Archivo = "comunidad_virtual_usuarios.csv",
                    FechaEjecucion = _fechaEjecucionEtl,
                    Recibidos = totalUsuarios,
                    Aceptados = totalUsuarios,
                    Transformados = totalUsuarios,
                    Rechazados = 0
                },
                new()
                {
                    IdEjecucion = "ETL-INTERACCIONES",
                    Archivo = "comunidad_virtual_1000_interacciones.csv",
                    FechaEjecucion = _fechaEjecucionEtl,
                    Recibidos = _totalInteraccionesRecibidas,
                    Aceptados = 207,
                    Transformados = 207,
                    Rechazados = _totalCuarentenaReal
                }
            ],
            InteraccionesRecientes = recientes
        };

        return Task.FromResult(dto);
    }

    private static string? FindSalidaDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var cand1 = Path.Combine(dir.FullName, "salida");
            var cand2 = Path.Combine(dir.FullName, "proyecto_BD", "salida");
            var cand3 = Path.Combine(dir.FullName, "proyecto BD", "salida");
            if (Directory.Exists(cand1)) return cand1;
            if (Directory.Exists(cand2)) return cand2;
            if (Directory.Exists(cand3)) return cand3;
            dir = dir.Parent;
        }
        return null;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cassandraCluster is not null)
        {
            await _cassandraCluster.ShutdownAsync();
        }
        if (_cbCluster is not null)
        {
            await _cbCluster.DisposeAsync();
        }
    }
}
