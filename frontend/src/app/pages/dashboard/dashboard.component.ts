import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { DashboardResumen, InteraccionResumen } from '../../core/models/auth.models';

type SeccionActiva = 'resumen' | 'vivo' | 'historico' | 'calidad' | 'api';

interface EventoVivo {
  id: number;
  metodo: 'POST' | 'PUT' | 'DELETE' | 'GET';
  codigo: number;
  resumen: string;
  hora: string;
}

interface SegmentoDona {
  tipo: string;
  conteo: number;
  porcentaje: number;
  color: string;
  dashArray: string;
  dashOffset: number;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div id="app" [class.sidebar-collapsed]="!menuAbierto()">
      @if (menuAbierto()) {
        <div class="side-backdrop" (click)="menuAbierto.set(false)"></div>
      }

      <!-- Menú lateral izquierdo desplegable (sin iconos) -->
      <aside [class.closed]="!menuAbierto()">
        <div class="brand">
          <div class="logo">C360</div>
          <div class="brand-text">
            <b>Community 360</b>
            <span>Panel de administración</span>
          </div>
          <button
            type="button"
            class="toggle-side"
            (click)="menuAbierto.set(false)"
            title="Ocultar menú lateral"
          >
            ‹
          </button>
        </div>

        <nav>
          <a
            href="javascript:void(0)"
            [class.on]="seccion() === 'resumen'"
            (click)="cambiarSeccion('resumen')"
          >Resumen</a>
          <a
            href="javascript:void(0)"
            [class.on]="seccion() === 'vivo'"
            (click)="cambiarSeccion('vivo')"
          >Tiempo real</a>
          <a
            href="javascript:void(0)"
            [class.on]="seccion() === 'historico'"
            (click)="cambiarSeccion('historico')"
          >Histórico</a>
          <a
            href="javascript:void(0)"
            [class.on]="seccion() === 'calidad'"
            (click)="cambiarSeccion('calidad')"
          >Calidad y privacidad</a>
          <a
            href="javascript:void(0)"
            [class.on]="seccion() === 'api'"
            (click)="cambiarSeccion('api')"
          >Movimientos</a>
        </nav>

        <div class="user">
          @if (authService.currentUser(); as u) {
            <b>{{ u.nombre }}</b>
            <span class="user-mail">{{ u.correo }}</span>
          }
          <button type="button" class="out-btn" (click)="cerrarSesion()">
            Cerrar sesión
          </button>
        </div>
      </aside>

      <!-- Contenido principal -->
      <main>
        @if (!menuAbierto()) {
          <div class="open-bar">
            <button
              type="button"
              class="open-side-btn"
              (click)="menuAbierto.set(true)"
            >
              ☰ Menú
            </button>
          </div>
        }
        <!-- =============================================================== -->
        <!-- 1. RESUMEN GENERAL                                              -->
        <!-- =============================================================== -->
        @if (seccion() === 'resumen') {
          <section class="on">
            <div class="head">
              <h2>Resumen general</h2>
            </div>

            @if (resumen(); as r) {
              <div class="grid">
                <div class="card c6">
                  <div class="db">
                    <b>Couchbase Capella</b>
                    <span class="tag">community360.comunidad</span>
                  </div>
                  <div class="mini">
                    <div>
                      <span>usuarios</span>
                      <b>{{ r.total_usuarios | number }}</b>
                    </div>
                    <div>
                      <span>cuarentena</span>
                      <b>{{ r.total_cuarentena | number }}</b>
                    </div>
                    <div>
                      <span>bitácora carga</span>
                      <b>{{ r.total_bitacora_carga }}</b>
                    </div>
                    <div>
                      <span>cuentas</span>
                      <b>{{ r.total_cuentas_admin }}</b>
                    </div>
                  </div>
                </div>

                <div class="card c6">
                  <div class="db">
                    <b>DataStax Astra DB (Cassandra)</b>
                    <span class="tag">community360</span>
                  </div>
                  <div class="mini five-cols">
                    <div>
                      <span>por id</span>
                      <b>{{ r.total_interacciones_validas }}</b>
                    </div>
                    <div>
                      <span>por usuario</span>
                      <b>{{ r.total_interacciones_usuario }}</b>
                    </div>
                    <div>
                      <span>por tema</span>
                      <b>{{ r.total_interacciones_tema }}</b>
                    </div>
                    <div>
                      <span>por estado</span>
                      <b>{{ r.total_interacciones_estado }}</b>
                    </div>
                    <div>
                      <span>por etiqueta</span>
                      <b>{{ r.total_interacciones_etiqueta }}</b>
                    </div>
                  </div>
                </div>

                <div class="card c4">
                  <h3>Usuarios por estatus</h3>
                  @for (item of r.usuarios_por_estatus; track item.categoria) {
                    <div class="bar">
                      <span [title]="sinGuion(item.categoria)">{{ sinGuion(item.categoria) }}</span>
                      <span class="t">
                        <i [style.width.%]="pct(item.total, maxConteo(r.usuarios_por_estatus))"></i>
                      </span>
                      <em>{{ item.total }}</em>
                    </div>
                  }
                </div>

                <div class="card c4">
                  <h3>Interacciones por tema</h3>
                  @for (item of r.interacciones_por_tema; track item.categoria) {
                    <div class="bar">
                      <span [title]="sinGuion(item.categoria)">{{ sinGuion(item.categoria) }}</span>
                      <span class="t">
                        <i [style.width.%]="pct(item.total, maxConteo(r.interacciones_por_tema))"></i>
                      </span>
                      <em>{{ item.total }}</em>
                    </div>
                  }
                </div>

                <div class="card c4">
                  <h3>Interacciones por etiqueta</h3>
                  @for (item of r.interacciones_por_etiqueta_top; track item.categoria) {
                    <div class="bar">
                      <span [title]="sinGuion(item.categoria)">{{ sinGuion(item.categoria) }}</span>
                      <span class="t">
                        <i [style.width.%]="pct(item.total, maxConteo(r.interacciones_por_etiqueta_top))"></i>
                      </span>
                      <em>{{ item.total }}</em>
                    </div>
                  }
                </div>
              </div>
            }
          </section>
        }

        <!-- =============================================================== -->
        <!-- 2. TIEMPO REAL (100% DATOS REALES + WEBSOCKET ws://.../ws)      -->
        <!-- =============================================================== -->
        @if (seccion() === 'vivo') {
          <section id="vivo" class="on">
            <div class="head">
              <h2>Tiempo real</h2>
              <div class="pill" [class.off]="!wsConectado()">
                <span class="dot"></span>
                <span>{{ wsConectado() ? 'Conectado' : 'Desconectado' }}</span>
                @if (!wsConectado()) {
                  <button type="button" (click)="conectarWebSocket()">Reconectar</button>
                }
              </div>
            </div>

            @if (resumen(); as r) {
              <!-- Fila 1: 4 tarjetas KPI simétricas de altura fija con datos reales -->
              <div class="grid vivo-kpis">
                <div class="card kpi c3" [class.flash]="flashKpi()">
                  <h3>Interacciones vigentes</h3>
                  <b>{{ r.total_interacciones_validas }}</b>
                </div>

                <div class="card kpi c3" [class.flash]="flashKpi()">
                  <h3>% reacciones</h3>
                  <b>{{ r.porcentaje_reacciones }}%</b>
                </div>

                <div class="card kpi c3 kpi-leader" [class.flash]="flashKpi()">
                  <h3>Tema líder</h3>
                  <b [title]="sinGuion(r.tema_lider)">{{ sinGuion(r.tema_lider) }}</b>
                </div>

                <div class="card kpi c3" [class.flash]="flashKpi()">
                  <h3>Eventos recibidos</h3>
                  <b>{{ eventosRecibidos() }}</b>
                </div>
              </div>

              <!-- Fila 2: 3 tarjetas gráficas simétricas (Dona, Barras y Mapa de calor Tema × Canal) -->
              <div class="grid vivo-mid">
                <div class="card c4 vivo-box">
                  <h3>Por tipo</h3>
                  <div class="dw-sym">
                    <svg viewBox="0 0 42 42" class="donut-svg">
                      <circle cx="21" cy="21" r="15.9" fill="none" stroke="#e3f5ea" stroke-width="5.5" />
                      @for (seg of segmentosDona(); track seg.tipo) {
                        <circle
                          cx="21"
                          cy="21"
                          r="15.9"
                          fill="none"
                          [attr.stroke]="seg.color"
                          stroke-width="5.5"
                          [attr.stroke-dasharray]="seg.dashArray"
                          [attr.stroke-dashoffset]="seg.dashOffset"
                        />
                      }
                    </svg>
                    <div class="leg">
                      @for (seg of segmentosDona(); track seg.tipo) {
                        <div class="leg-row">
                          <i [style.background]="seg.color"></i>
                          <span class="leg-name">{{ sinGuion(seg.tipo) }}</span>
                          <b>{{ seg.conteo }}</b>
                          <small>({{ seg.porcentaje }}%)</small>
                        </div>
                      }
                    </div>
                  </div>
                </div>

                <div class="card c4 vivo-box">
                  <h3>Por tema</h3>
                  <div class="bars-compact">
                    @for (t of r.interacciones_por_tema; track t.categoria) {
                      <div class="bar bar-sm">
                        <span [title]="sinGuion(t.categoria)">{{ sinGuion(t.categoria) }}</span>
                        <span class="t">
                          <i [style.width.%]="pct(t.total, maxConteo(r.interacciones_por_tema))"></i>
                        </span>
                        <em>{{ t.total }}</em>
                      </div>
                    }
                  </div>
                </div>

                <div class="card c4 vivo-box">
                  <h3>Mapa de calor · tema × canal</h3>
                  <div class="hm-wrap">
                    <div class="hm-tc">
                      <div></div>
                      @for (abrev of canalesAbrev; track abrev) {
                        <div class="hm-head">{{ abrev }}</div>
                      }
                      @for (fila of r.mapa_calor_tema_canal; track fila.tema) {
                        <div class="hm-tema" [title]="sinGuion(fila.tema)">{{ sinGuion(fila.tema) }}</div>
                        @for (val of fila.celdas; track $index; let cIdx = $index) {
                          <div
                            class="c-tc"
                            [title]="sinGuion(fila.tema) + ' · ' + (r.canales_calor[cIdx] || '') + ': ' + val"
                            [style.background]="colorCeldaCalor(val, maxHeatmapTemaCanal())"
                            [style.color]="val / maxHeatmapTemaCanal() > 0.55 ? '#fff' : '#16322a'"
                          >
                            {{ val }}
                          </div>
                        }
                      }
                    </div>
                  </div>
                </div>
              </div>
            }
          </section>
        }

        <!-- =============================================================== -->
        <!-- 3. CONSULTA HISTÓRICA (SOLO LECTURA REST - FASE 5 y 6)          -->
        <!-- =============================================================== -->
        @if (seccion() === 'historico') {
          <section class="on">
            <div class="head">
              <h2>Consulta histórica</h2>
            </div>

            <div class="grid">
              <div class="card c12">
                <div class="filters">
                  <input
                    placeholder="Usuario (ej. U000002)"
                    [(ngModel)]="filtroUsuario"
                    (keydown.enter)="buscarInteracciones()"
                  />
                  <select [(ngModel)]="filtroTema">
                    <option value="">Todos los temas</option>
                    @for (tm of temas; track tm) {
                      <option [value]="tm">{{ sinGuion(tm) }}</option>
                    }
                  </select>
                  <select [(ngModel)]="filtroCanal">
                    <option value="">Todos los canales</option>
                    @for (c of canales; track c) {
                      <option [value]="c">{{ c }}</option>
                    }
                  </select>
                  <select [(ngModel)]="filtroEtiqueta">
                    <option value="">Todas las etiquetas</option>
                    @for (et of etiquetasCatalogo; track et) {
                      <option [value]="et">{{ sinGuion(et) }}</option>
                    }
                  </select>
                  <button type="button" class="p" (click)="buscarInteracciones()">Consultar</button>
                  <button type="button" (click)="limpiarFiltros()">Limpiar</button>
                </div>

                <div class="tw">
                  <table>
                    <thead>
                      <tr>
                        <th>ID</th>
                        <th>Usuario</th>
                        <th>Fecha</th>
                        <th>Canal</th>
                        <th>Tipo</th>
                        <th>Tema</th>
                        <th>Texto</th>
                        <th>Estado</th>
                        <th>Sent.</th>
                        <th>Etiquetas</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (i of listaInteracciones(); track i.id_interaccion) {
                        <tr>
                          <td><b>{{ i.id_interaccion }}</b></td>
                          <td>{{ i.id_usuario }}</td>
                          <td>{{ i.fecha_hora }}</td>
                          <td>{{ i.canal }}</td>
                          <td>{{ sinGuion(i.tipo) }}</td>
                          <td>{{ sinGuion(i.tema) }}</td>
                          <td class="td-clip" [title]="i.texto || ''">{{ i.texto || '—' }}</td>
                          <td>{{ i.estado || '—' }}</td>
                          <td>{{ i.sentimiento | number:'1.2-2' }}</td>
                          <td>
                            @for (et of i.etiquetas; track et) {
                              <span class="chip">{{ sinGuion(et) }}</span>
                            }
                          </td>
                        </tr>
                      } @empty {
                        <tr>
                          <td colspan="10" style="color:var(--mut);text-align:center;padding:18px">
                            Sin resultados para los filtros seleccionados
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              </div>

              <div class="card c5">
                <h3>Por canal</h3>
                @for (c of conteoPorCanal(); track c.categoria) {
                  <div class="bar">
                    <span>{{ c.categoria }}</span>
                    <span class="t">
                      <i [style.width.%]="pct(c.total, maxConteo(conteoPorCanal()))"></i>
                    </span>
                    <em>{{ c.total }}</em>
                  </div>
                }
              </div>

              <div class="card c7">
                <h3>Tendencia por fecha</h3>
                <svg viewBox="0 0 400 130" style="width:100%;height:150px">
                  @if (svgTendencia(); as svg) {
                    <polygon [attr.points]="svg.areaPoints" fill="#2e9e6b" opacity=".13" />
                    <polyline [attr.points]="svg.linePoints" fill="none" stroke="#2e9e6b" stroke-width="2.5" stroke-linejoin="round" />
                    @for (pt of svg.circles; track pt.fecha) {
                      <circle [attr.cx]="pt.x" [attr.cy]="pt.y" r="3" fill="#fff" stroke="#2e9e6b" stroke-width="2">
                        <title>{{ pt.fecha }}: {{ pt.total }} interacciones</title>
                      </circle>
                    }
                  }
                </svg>
              </div>
            </div>
          </section>
        }

        <!-- =============================================================== -->
        <!-- 4. CALIDAD Y PRIVACIDAD                                         -->
        <!-- =============================================================== -->
        @if (seccion() === 'calidad') {
          <section class="on">
            <div class="head">
              <h2>Calidad y privacidad</h2>
            </div>

            @if (resumen(); as r) {
              <div class="grid">
                <div class="card c6">
                  <h3>Resumen de calidad: antes y después</h3>
                  <div class="ba">
                    <div>
                      <small>Interacciones recibidas</small>
                      <b>{{ r.total_interacciones_recibidas | number }}</b>
                    </div>
                    <div class="ar">→</div>
                    <div>
                      <small>Válidas cargadas</small>
                      <b style="color:var(--g)">{{ r.total_interacciones_validas | number }}</b>
                    </div>
                  </div>
                  <div class="ba" style="margin-top:14px">
                    <div>
                      <small>Usuarios recibidos</small>
                      <b>{{ r.total_usuarios | number }}</b>
                    </div>
                    <div class="ar">→</div>
                    <div>
                      <small>Usuarios cargados</small>
                      <b style="color:var(--g)">{{ r.total_usuarios | number }}</b>
                    </div>
                  </div>
                </div>

                <div class="card c6">
                  <h3>Registro sin consentimiento ({{ r.ejemplo_usuario_anonimizado?.id_usuario }})</h3>
                  <pre>{{ ejemploAnonimizadoJson() }}</pre>
                </div>

                <div class="card c6">
                  <h3>Bitácora de carga</h3>
                  <div class="tw">
                    <table>
                      <thead>
                        <tr>
                          <th>Ejecución</th>
                          <th>Archivo</th>
                          <th>Leídos</th>
                          <th>Válidos</th>
                          <th>Rech.</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (b of r.bitacora_carga; track b.id_ejecucion) {
                          <tr>
                            <td><b>{{ b.id_ejecucion }}</b></td>
                            <td>{{ sinGuion(b.archivo) }}</td>
                            <td>{{ b.recibidos | number }}</td>
                            <td>{{ b.aceptados | number }}</td>
                            <td>{{ b.rechazados | number }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>

                <div class="card c6">
                  <h3>Cuarentena</h3>
                  <div class="tw">
                    <table>
                      <thead>
                        <tr>
                          <th>ID interacción</th>
                          <th>ID usuario</th>
                          <th>Canal</th>
                          <th>Motivo</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (c of r.muestra_cuarentena.slice(0, 5); track c.id_interaccion) {
                          <tr>
                            <td><b>{{ c.id_interaccion }}</b></td>
                            <td>{{ c.id_usuario }}</td>
                            <td>{{ c.canal }}</td>
                            <td><span class="chip">{{ sinGuion(c.motivo_rechazo) }}</span></td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>

                <div class="card c6">
                  <h3>Valores atípicos de actividad</h3>
                  <div class="tw">
                    <table>
                      <thead>
                        <tr>
                          <th>Usuario</th>
                          <th>Mensajes (30d)</th>
                          <th>Reacciones (30d)</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (a of r.atipicos_actividad; track a.id_usuario) {
                          <tr>
                            <td><b>{{ a.id_usuario }}</b></td>
                            <td>{{ a.mensajes_30d }}</td>
                            <td>{{ a.reacciones_30d }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>

                <div class="card c6">
                  <h3>Privacidad</h3>
                  <div class="dw-priv">
                    <div class="kpi">
                      <b>{{ r.usuarios_con_consentimiento }}</b>
                      <small>con consentimiento</small>
                    </div>
                    <div class="kpi">
                      <b style="color:var(--r)">{{ r.usuarios_sin_consentimiento }}</b>
                      <small>sin consentimiento: {{ r.valores_geo_borrados_usuarios }} valores geográficos suprimidos</small>
                    </div>
                  </div>
                </div>
              </div>
            }
          </section>
        }

        <!-- =============================================================== -->
        <!-- 5. MOVIMIENTOS Y EVENTOS EN VIVO                                -->
        <!-- =============================================================== -->
        @if (seccion() === 'api') {
          <section class="on">
            <div class="head">
              <h2>Movimientos</h2>
            </div>

            <div class="grid vivo-bot">
              <div class="card c6 vivo-bot-box">
                <h3>Eventos en vivo</h3>
                <ul id="log">
                  @for (ev of bitacoraEventos(); track ev.id) {
                    <li>
                      <span class="m" [ngClass]="ev.metodo">{{ ev.metodo }}</span>
                      <span class="ev-txt">{{ sinGuion(ev.resumen) }}</span>
                      <span class="ev-meta">{{ ev.codigo }} · {{ ev.hora }}</span>
                    </li>
                  } @empty {
                    <li style="color:var(--mut);border:0">
                      Esperando operaciones POST, PUT o DELETE sobre /api/interacciones…
                    </li>
                  }
                </ul>
              </div>

              <div class="card c6 vivo-bot-box">
                <h3>Evidencia: petición → respuesta → cambio</h3>
                <div class="rq-stack">
                  <div class="rq-block">
                    <h4>Petición HTTP</h4>
                    <pre>{{ ultimaPeticion() }}</pre>
                  </div>
                  <div class="rq-block">
                    <h4>Respuesta HTTP</h4>
                    <pre>{{ ultimaRespuesta() }}</pre>
                  </div>
                </div>
                @if (ultimoEfecto()) {
                  <div class="eff">{{ sinGuion(ultimoEfecto()) }}</div>
                }
              </div>
            </div>
          </section>
        }
      </main>
    </div>
  `,
  styles: [`
    #app {
      display: grid;
      grid-template-columns: 248px 1fr;
      min-height: 100vh;
      transition: grid-template-columns 0.22s ease;
    }
    #app.sidebar-collapsed {
      grid-template-columns: 0px 1fr;
    }
    aside {
      background: var(--side);
      border-right: 1px solid var(--bd);
      padding: 20px 14px;
      display: flex;
      flex-direction: column;
      gap: 4px;
      position: sticky;
      top: 0;
      height: 100vh;
      width: 248px;
      overflow: hidden;
      transition: transform 0.22s ease, opacity 0.18s ease;
      z-index: 40;
    }
    aside.closed {
      transform: translateX(-100%);
      opacity: 0;
      pointer-events: none;
      border-right: 0;
    }
    .brand {
      display: flex;
      gap: 10px;
      align-items: center;
      margin-bottom: 20px;
      padding: 0 2px;
    }
    .brand .logo {
      width: 38px;
      height: 38px;
      border-radius: 12px;
      background: linear-gradient(135deg, var(--g), var(--t));
      display: grid;
      place-items: center;
      color: #fff;
      font-weight: 800;
      font-size: 13px;
      flex-shrink: 0;
    }
    .brand-text {
      min-width: 0;
      flex: 1;
    }
    .brand b {
      display: block;
      font-size: 15.5px;
      color: var(--fg);
      white-space: nowrap;
    }
    .brand span {
      font-size: 11px;
      color: var(--mut);
      white-space: nowrap;
    }
    .toggle-side {
      width: 30px;
      height: 30px;
      padding: 0;
      border-radius: 8px;
      display: grid;
      place-items: center;
      font-size: 18px;
      line-height: 1;
      color: var(--mut);
      background: var(--bg);
      border: 1px solid var(--bd);
      flex-shrink: 0;
    }
    .toggle-side:hover {
      background: var(--gl);
      color: var(--gd);
      border-color: var(--g);
    }
    nav {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    nav a {
      display: block;
      padding: 10px 14px;
      border-radius: 10px;
      color: var(--mut);
      text-decoration: none;
      font-weight: 500;
      font-size: 13.5px;
      white-space: nowrap;
      transition: background 0.15s, color 0.15s;
    }
    nav a:hover {
      background: var(--gl);
      color: var(--gd);
    }
    nav a.on {
      background: var(--g);
      color: #fff;
      font-weight: 600;
    }
    .user {
      margin-top: auto;
      border-top: 1px solid var(--bd);
      padding-top: 14px;
      font-size: 12px;
      color: var(--mut);
    }
    .user b {
      display: block;
      color: var(--fg);
      font-size: 13px;
      margin-bottom: 2px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .user-mail {
      display: block;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .out-btn {
      margin-top: 10px;
      width: 100%;
    }
    .side-backdrop {
      display: none;
    }
    .open-bar {
      margin-bottom: 14px;
    }
    .open-side-btn {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 7px 13px;
      background: #fff;
      border: 1px solid var(--bd);
      border-radius: 10px;
      color: var(--gd);
      font-weight: 600;
      font-size: 13px;
      box-shadow: var(--sh);
    }
    .open-side-btn:hover {
      background: var(--gl);
      border-color: var(--g);
    }
    main {
      padding: 24px 28px;
      min-width: 0;
      max-width: 1440px;
      width: 100%;
      box-sizing: border-box;
    }
    .head {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
      flex-wrap: wrap;
    }
    .head h2 {
      margin: 0;
      font-size: 24px;
      font-weight: 700;
      letter-spacing: -0.01em;
    }
    .head .s {
      color: var(--mut);
      font-size: 14px;
    }
    .pill {
      margin-left: auto;
      display: flex;
      gap: 8px;
      align-items: center;
      font-size: 12px;
      color: var(--mut);
      background: #fff;
      border: 1px solid var(--bd);
      border-radius: 99px;
      padding: 6px 12px;
    }
    .dot {
      width: 9px;
      height: 9px;
      border-radius: 50%;
      background: var(--g);
      animation: pl 1.4s infinite;
    }
    .off .dot {
      background: var(--y);
      animation: none;
    }
    @keyframes pl {
      50% { box-shadow: 0 0 0 6px rgba(46, 158, 107, 0.2); }
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(12, 1fr);
      gap: 16px;
    }
    .card {
      background: var(--card);
      border: 1px solid var(--bd);
      border-radius: 16px;
      padding: 18px;
      min-width: 0;
      box-shadow: var(--sh);
    }
    .card h3 {
      margin: 0 0 12px;
      font-size: 11.5px;
      color: var(--mut);
      text-transform: uppercase;
      letter-spacing: 0.85px;
      font-weight: 700;
    }
    .c3 { grid-column: span 3; }
    .c4 { grid-column: span 4; }
    .c5 { grid-column: span 5; }
    .c6 { grid-column: span 6; }
    .c7 { grid-column: span 7; }
    .c12 { grid-column: span 12; }

    .db {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
      gap: 8px;
    }
    .db b {
      font-size: 15px;
    }
    .tag {
      font-size: 11px;
      padding: 3px 10px;
      border-radius: 99px;
      background: var(--gl);
      color: var(--gd);
      font-weight: 600;
    }
    .mini {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
      gap: 10px;
    }
    .mini.five-cols {
      grid-template-columns: repeat(auto-fit, minmax(96px, 1fr));
    }
    .mini div {
      background: var(--bg);
      border: 1px solid var(--bd);
      border-radius: 12px;
      padding: 11px;
      display: flex;
      flex-direction: column;
      justify-content: space-between;
    }
    .mini span {
      display: block;
      color: var(--mut);
      font-size: 11px;
    }
    .mini b {
      font-size: 21px;
      color: var(--gd);
      margin: 3px 0;
    }
    .bar {
      display: grid;
      grid-template-columns: 140px 1fr 38px;
      gap: 8px;
      align-items: center;
      margin: 9px 0;
      font-size: 13px;
    }
    .bar span:first-child {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .bar .t {
      background: var(--gl);
      border-radius: 9px;
      height: 9px;
      overflow: hidden;
    }
    .bar i {
      display: block;
      height: 9px;
      border-radius: 9px;
      background: var(--g);
      transition: width 0.45s ease;
    }
    .bar em {
      font-style: normal;
      text-align: right;
      font-variant-numeric: tabular-nums;
      font-weight: 600;
    }

    /* Sección Tiempo Real Minimalista y Simétrica */
    .vivo-kpis, .vivo-mid, .vivo-bot {
      gap: 14px;
      margin-bottom: 14px;
      align-items: stretch;
    }
    #vivo .card {
      box-shadow: none;
      padding: 16px;
      display: flex;
      flex-direction: column;
    }
    .kpi b {
      font-size: 26px;
      line-height: 32px;
      height: 32px;
      display: block;
      color: var(--gd);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .kpi-leader b {
      font-size: 18px;
    }
    .kpi small {
      color: var(--mut);
      font-size: 11px;
      display: block;
      margin-top: 4px;
    }
    .vivo-box {
      min-height: 256px;
    }
    .dw-sym {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 16px;
      flex: 1;
      flex-wrap: wrap;
    }
    .donut-svg {
      width: 120px;
      height: 120px;
      flex-shrink: 0;
    }
    .donut-svg circle {
      transition: stroke-dasharray 0.45s ease, stroke-dashoffset 0.45s ease;
    }
    .leg {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 0;
    }
    .leg-row {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11.5px;
      color: var(--fg);
    }
    .leg-row i {
      width: 9px;
      height: 9px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .leg-name {
      color: var(--mut);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .leg-row small {
      color: var(--mut);
    }
    .bars-compact {
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      flex: 1;
    }
    .bar-sm {
      grid-template-columns: 115px 1fr 30px;
      font-size: 12px;
      margin: 2px 0;
    }
    .hm-wrap {
      display: flex;
      align-items: center;
      flex: 1;
      overflow-x: auto;
    }
    .hm-tc {
      width: 100%;
      min-width: 280px;
      display: grid;
      grid-template-columns: 98px repeat(6, 1fr);
      gap: 4px;
      font-size: 10.5px;
      color: var(--mut);
      align-items: center;
    }
    .hm-head {
      text-align: center;
      font-weight: 700;
      font-size: 10px;
    }
    .hm-tema {
      font-size: 11px;
      color: var(--fg);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      padding-right: 4px;
    }
    .c-tc {
      height: 22px;
      border-radius: 5px;
      display: grid;
      place-items: center;
      font-size: 10.5px;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
      transition: background 0.35s ease, color 0.35s ease;
    }
    .vivo-bot-box {
      min-height: 290px;
    }
    #log {
      list-style: none;
      margin: 0;
      padding: 0;
      overflow-y: auto;
      flex: 1;
    }
    #log li {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 7px 0;
      border-bottom: 1px dashed var(--bd);
      font-size: 12px;
    }
    .ev-txt {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .ev-meta {
      margin-left: auto;
      color: var(--mut);
      font-size: 11px;
      flex-shrink: 0;
    }
    .m {
      font-weight: 700;
      font-size: 10px;
      padding: 2px 7px;
      border-radius: 5px;
      min-width: 52px;
      text-align: center;
      color: #fff;
      display: inline-block;
    }
    .POST { background: var(--g); }
    .PUT { background: var(--y); }
    .DELETE { background: var(--r); }
    .GET { background: var(--t); }

    .rq-stack {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 10px;
      flex: 1;
      min-height: 0;
    }
    .rq-block {
      display: flex;
      flex-direction: column;
      min-height: 0;
    }
    .rq-block h4 {
      margin: 0 0 5px;
      font-size: 11.5px;
      color: var(--mut);
    }
    .rq-block pre {
      flex: 1;
      max-height: 155px;
    }
    pre {
      margin: 0;
      background: #f0f7f3;
      border: 1px solid var(--bd);
      border-radius: 10px;
      padding: 10px 12px;
      font-size: 12px;
      line-height: 1.45;
      overflow: auto;
      white-space: pre-wrap;
      word-break: break-word;
    }
    .eff {
      margin-top: 10px;
      background: var(--gl);
      border-radius: 10px;
      padding: 9px 12px;
      font-size: 12.5px;
      color: var(--gd);
      font-weight: 500;
    }
    .eff.eff-err {
      background: #fdecec;
      color: #b83336;
    }
    .flash {
      animation: fl 0.65s ease;
    }
    @keyframes fl {
      0% { background: #d4f4e2; }
    }

    /* Histórico y Tablas */
    .filters {
      display: grid;
      grid-template-columns: 1.2fr 1fr 1fr 1fr auto auto;
      gap: 8px;
      margin-bottom: 14px;
      align-items: center;
    }
    .crud-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 10px;
      align-items: end;
    }
    .crud-grid label {
      display: block;
      font-size: 12px;
      font-weight: 600;
      color: var(--mut);
      margin-bottom: 4px;
    }
    .crud-text {
      grid-column: span 3;
    }
    .crud-btn {
      grid-column: span 1;
    }
    .tw {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
      white-space: nowrap;
    }
    th {
      color: var(--mut);
      font-weight: 600;
      text-align: left;
      padding: 9px 8px;
      border-bottom: 2px solid var(--bd);
      font-size: 12px;
    }
    td {
      padding: 9px 8px;
      border-bottom: 1px solid #edf5f0;
    }
    tr:hover td {
      background: #f7fcf9;
    }
    .td-clip {
      max-width: 210px;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .chip {
      background: var(--gl);
      color: var(--gd);
      border-radius: 6px;
      padding: 2px 7px;
      font-size: 11px;
      margin-right: 3px;
      font-weight: 500;
    }
    .btn-xs {
      padding: 3px 8px;
      font-size: 11.5px;
      border-radius: 6px;
      margin-right: 4px;
    }
    .btn-del {
      color: var(--r);
      border-color: #f3c6c7;
    }
    .btn-del:hover {
      background: #fdecec;
      border-color: var(--r);
    }

    /* Calidad e Indicadores */
    .ba {
      display: grid;
      grid-template-columns: 1fr 40px 1fr;
      align-items: center;
      gap: 8px;
      text-align: center;
      background: var(--bg);
      border: 1px solid var(--bd);
      border-radius: 12px;
      padding: 10px;
    }
    .ba b {
      font-size: 24px;
      display: block;
    }
    .ba small {
      color: var(--mut);
      font-size: 11.5px;
    }
    .ba .ar {
      font-size: 22px;
      color: var(--g);
      font-weight: 700;
    }
    .dw-priv {
      display: flex;
      gap: 32px;
      align-items: center;
      flex-wrap: wrap;
      padding: 8px 0;
    }
    .def {
      font-size: 12.5px;
      color: var(--mut);
      line-height: 1.65;
    }
    .def code {
      background: var(--gl);
      color: var(--gd);
      padding: 2px 7px;
      border-radius: 5px;
      font-weight: 600;
    }
    .def-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 6px 0;
      border-bottom: 1px dashed var(--bd);
      gap: 10px;
    }

    @media (max-width: 1200px) {
      .c3 { grid-column: span 6; }
      .c4 { grid-column: span 6; }
      .c4:last-child { grid-column: span 12; }
      .filters {
        grid-template-columns: repeat(3, 1fr);
      }
    }

    @media (max-width: 960px) {
      #app,
      #app.sidebar-collapsed {
        grid-template-columns: 1fr;
      }
      .side-backdrop {
        display: block;
        position: fixed;
        inset: 0;
        background: rgba(15, 42, 29, 0.32);
        backdrop-filter: blur(2px);
        z-index: 90;
      }
      aside {
        position: fixed;
        top: 0;
        left: 0;
        bottom: 0;
        height: 100vh;
        width: 260px;
        max-width: 82vw;
        z-index: 100;
        box-shadow: 0 12px 32px rgba(15, 42, 29, 0.18);
        overflow-y: auto;
      }
      main {
        padding: 18px 20px;
      }
      .c5, .c6, .c7 {
        grid-column: span 12;
      }
      .rq-stack {
        grid-template-columns: 1fr;
      }
      .vivo-box, .vivo-bot-box {
        min-height: auto;
        height: auto;
      }
      .filters {
        grid-template-columns: repeat(2, 1fr);
      }
    }

    @media (max-width: 640px) {
      main {
        padding: 14px;
      }
      .head h2 {
        font-size: 20px;
      }
      .pill {
        margin-left: 0;
        width: 100%;
        justify-content: center;
      }
      .c3, .c4, .c5, .c6, .c7, .c12 {
        grid-column: 1 / -1 !important;
      }
      .filters {
        grid-template-columns: 1fr;
      }
      .bar {
        grid-template-columns: 110px 1fr 34px;
        font-size: 12px;
      }
      .ba {
        grid-template-columns: 1fr;
        gap: 6px;
      }
      .ba .ar {
        transform: rotate(90deg);
      }
      .dw-priv {
        gap: 16px;
        justify-content: center;
      }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  readonly authService = inject(AuthService);

  readonly seccion = signal<SeccionActiva>('resumen');
  readonly menuAbierto = signal(true);
  readonly resumen = signal<DashboardResumen | null>(null);
  readonly listaInteracciones = signal<InteraccionResumen[]>([]);

  // CRUD e Histórico
  readonly mostrarFormularioCrud = signal(false);
  readonly guardando = signal(false);
  readonly idEditando = signal<string | null>(null);
  readonly mensajeApi = signal<string | null>(null);
  readonly esErrorApi = signal(false);
  readonly trazaConsultaGet = signal('GET /api/interacciones\nAuthorization: Bearer ••••••\n→ 200 OK');

  readonly canales = ['Telegram', 'Discord', 'Instagram', 'TikTok', 'Facebook', 'Twitter/X'];
  readonly tipos = ['mensaje', 'comentario', 'reaccion', 'respuesta_encuesta', 'rsvp_evento'];
  readonly temas = [
    'arte_comisiones',
    'reglas_moderacion',
    'videojuegos',
    'eventos_convenciones',
    'seguridad_privacidad',
    'fursuits',
    'musica'
  ];
  readonly dispositivos = ['Android', 'iOS', 'Web', 'Escritorio'];
  readonly etiquetasCatalogo = ['evento', 'humor', 'arte', 'aviso', 'juegos', 'reglas', 'musica'];

  filtroUsuario = '';
  filtroTema = '';
  filtroCanal = '';
  filtroEtiqueta = '';

  formInteraccion = {
    id_usuario: 'U000002',
    canal: 'Telegram',
    tipo: 'mensaje',
    tema: 'videojuegos',
    sentimiento: 0.5,
    reacciones_recibidas: 0,
    dispositivo: 'Web',
    etiquetasTexto: 'juegos',
    texto: ''
  };

  // WebSocket Real conectado al backend .NET 10 (ws://localhost:5000/ws)
  private ws: WebSocket | null = null;
  readonly wsConectado = signal(false);
  readonly flashKpi = signal(false);
  readonly eventosRecibidos = signal(0);
  readonly bitacoraEventos = signal<EventoVivo[]>([]);
  readonly ultimaPeticion = signal('—');
  readonly ultimaRespuesta = signal('—');
  readonly ultimoEfecto = signal('');

  readonly canalesAbrev = ['TG', 'DC', 'IG', 'TK', 'FB', 'TW'];

  readonly maxHeatmapTemaCanal = computed(() => {
    const filas = this.resumen()?.mapa_calor_tema_canal;
    if (!filas || filas.length === 0) return 1;
    return Math.max(...filas.flatMap((f) => f.celdas), 1);
  });

  colorCeldaCalor(val: number, maxVal: number): string {
    if (val <= 0) return '#f0f7f3';
    const alpha = (0.18 + (val / Math.max(maxVal, 1)) * 0.82).toFixed(2);
    return `rgba(46, 158, 107, ${alpha})`;
  }

  readonly segmentosDona = computed<SegmentoDona[]>(() => {
    const lista = this.resumen()?.interacciones_por_tipo || [];
    const colores = ['#2e9e6b', '#17a2a2', '#e9a23b', '#7b6cf0', '#e5686a'];
    const n = Math.max(
      lista.reduce((acc, x) => acc + x.total, 0),
      1
    );
    let offset = 0;
    return lista.map((item, i) => {
      const pctVal = (item.total / n) * 100;
      const seg: SegmentoDona = {
        tipo: item.categoria,
        conteo: item.total,
        porcentaje: Number(pctVal.toFixed(1)),
        color: colores[i % colores.length],
        dashArray: `${pctVal} ${100 - pctVal}`,
        dashOffset: 25 - offset
      };
      offset += pctVal;
      return seg;
    });
  });

  readonly conteoPorCanal = computed(() => {
    const items = this.listaInteracciones();
    if (items.length === 0) {
      return this.resumen()?.interacciones_por_canal || [];
    }
    const counts: Record<string, number> = {};
    for (const item of items) {
      counts[item.canal] = (counts[item.canal] || 0) + 1;
    }
    return Object.entries(counts)
      .map(([categoria, total]) => ({ categoria, total }))
      .sort((a, b) => b.total - a.total);
  });

  readonly svgTendencia = computed(() => {
    const serie = this.resumen()?.tendencia_diaria || [];
    if (serie.length === 0) return null;
    const maxVal = Math.max(...serie.map((s) => s.total), 1);
    const stepX = serie.length > 1 ? 380 / (serie.length - 1) : 0;

    const circles = serie.map((s, i) => ({
      x: Number((10 + i * stepX).toFixed(1)),
      y: Number((120 - (s.total / maxVal) * 95).toFixed(1)),
      fecha: s.categoria,
      total: s.total
    }));

    const linePoints = circles.map((c) => `${c.x},${c.y}`).join(' ');
    const areaPoints = `10,120 ${linePoints} ${circles[circles.length - 1].x},120`;
    return { circles, linePoints, areaPoints };
  });

  readonly ejemploAnonimizadoJson = computed(() => {
    const u = this.resumen()?.ejemplo_usuario_anonimizado;
    if (!u) return '{}';
    return JSON.stringify(u, null, 2);
  });

  ngOnInit(): void {
    if (typeof window !== 'undefined' && window.innerWidth <= 960) {
      this.menuAbierto.set(false);
    }
    this.authService.verifySession().subscribe();
    this.cargarDatos();
    this.conectarWebSocket();
  }

  ngOnDestroy(): void {
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }

  conectarWebSocket(): void {
    try {
      if (this.ws) {
        this.ws.close();
      }
      const host = window.location.hostname;
      const wsUrl = (host === 'localhost' || host === '127.0.0.1')
        ? 'ws://localhost:5000/ws'
        : `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}/ws`;
      this.ws = new WebSocket(wsUrl);
      this.ws.onopen = () => {
        this.wsConectado.set(true);
      };
      this.ws.onclose = () => {
        this.wsConectado.set(false);
      };
      this.ws.onerror = () => {
        this.wsConectado.set(false);
      };
      this.ws.onmessage = (event) => {
        try {
          const msg = JSON.parse(event.data);
          this.eventosRecibidos.update((n) => n + 1);
          this.bitacoraEventos.update((lista) => [
            {
              id: Date.now() + Math.random(),
              metodo: msg.metodo || 'POST',
              codigo: msg.codigo || 200,
              resumen: msg.resumen || '',
              hora: msg.hora || new Date().toLocaleTimeString('es-MX', { hour12: false })
            },
            ...lista.slice(0, 9)
          ]);
          if (msg.peticion) this.ultimaPeticion.set(msg.peticion);
          if (msg.respuesta) this.ultimaRespuesta.set(msg.respuesta);
          if (msg.efecto) this.ultimoEfecto.set(msg.efecto);
          if (msg.dashboard) {
            this.resumen.set(msg.dashboard);
          } else {
            this.cargarDatos();
          }
          this.buscarInteracciones();
          this.dispararFlash();
        } catch {
          // Ignorar mensaje malformado
        }
      };
    } catch {
      this.wsConectado.set(false);
    }
  }

  cambiarSeccion(sec: SeccionActiva): void {
    this.seccion.set(sec);
    if (typeof window !== 'undefined' && window.innerWidth <= 960) {
      this.menuAbierto.set(false);
    }
  }

  sinGuion(texto: string | null | undefined): string {
    if (!texto) return '';
    return texto.replace(/_/g, ' ');
  }

  seleccionarSeccion(sec: SeccionActiva): void {
    this.seccion.set(sec);
    this.menuAbierto.set(false);
  }

  etiquetaSeccion(sec: SeccionActiva): string {
    switch (sec) {
      case 'resumen': return 'Resumen';
      case 'vivo': return 'Tiempo real';
      case 'historico': return 'Histórico';
      case 'calidad': return 'Calidad y privacidad';
      case 'api': return 'Indicadores y API';
    }
  }

  pct(valor: number, maximo: number): number {
    if (maximo <= 0) return 0;
    return Math.min(100, Math.round((valor / maximo) * 100));
  }

  maxConteo(lista: { total: number }[]): number {
    if (!lista || lista.length === 0) return 1;
    return Math.max(...lista.map((x) => x.total), 1);
  }

  cargarDatos(): void {
    this.authService.getDashboardResumen().subscribe({
      next: (data) => {
        this.resumen.set(data);
        this.buscarInteracciones();
      }
    });
  }

  buscarInteracciones(): void {
    const u = this.filtroUsuario.trim();
    const t = this.filtroTema;
    const c = this.filtroCanal;
    const e = this.filtroEtiqueta;

    this.authService.getInteracciones({
      id_usuario: u || undefined,
      tema: t || undefined,
      canal: c || undefined,
      etiqueta: e || undefined,
      limite: 100
    }).subscribe({
      next: (res) => {
        this.listaInteracciones.set(res.datos);
        const q = new URLSearchParams();
        if (u) q.set('id_usuario', u);
        if (t) q.set('tema', t);
        if (c) q.set('canal', c);
        if (e) q.set('etiqueta', e);
        this.trazaConsultaGet.set(
          `GET /api/interacciones${q.toString() ? '?' + q.toString() : ''}\nAuthorization: Bearer ••••••\n→ 200 OK · ${res.datos.length} registro(s)`
        );
      }
    });
  }

  limpiarFiltros(): void {
    this.filtroUsuario = '';
    this.filtroTema = '';
    this.filtroCanal = '';
    this.filtroEtiqueta = '';
    this.buscarInteracciones();
  }

  onTipoChange(nuevoTipo: string): void {
    if (nuevoTipo === 'reaccion') {
      this.formInteraccion.texto = '';
    }
  }

  iniciarEdicion(item: InteraccionResumen): void {
    this.idEditando.set(item.id_interaccion);
    this.mostrarFormularioCrud.set(true);
    this.mensajeApi.set(null);
    this.formInteraccion = {
      id_usuario: item.id_usuario,
      canal: item.canal,
      tipo: item.tipo,
      tema: item.tema,
      sentimiento: item.sentimiento,
      reacciones_recibidas: item.reacciones_recibidas,
      dispositivo: item.dispositivo || 'Web',
      etiquetasTexto: (item.etiquetas || []).join(', '),
      texto: item.texto || ''
    };
  }

  cancelarEdicion(): void {
    this.idEditando.set(null);
    this.mensajeApi.set(null);
    this.formInteraccion = {
      id_usuario: 'U000002',
      canal: 'Telegram',
      tipo: 'mensaje',
      tema: 'videojuegos',
      sentimiento: 0.5,
      reacciones_recibidas: 0,
      dispositivo: 'Web',
      etiquetasTexto: 'juegos',
      texto: ''
    };
  }

  guardarInteraccion(): void {
    this.guardando.set(true);
    this.mensajeApi.set(null);
    this.esErrorApi.set(false);

    const etiquetas = this.formInteraccion.etiquetasTexto
      .split(/[,|;]/)
      .map((s) => s.trim().toLowerCase())
      .filter((s) => s.length > 0);

    const idEdit = this.idEditando();
    if (idEdit) {
      const payload = {
        canal: this.formInteraccion.canal,
        tipo: this.formInteraccion.tipo,
        tema: this.formInteraccion.tema,
        texto: this.formInteraccion.tipo === 'reaccion' ? null : this.formInteraccion.texto,
        sentimiento: Number(this.formInteraccion.sentimiento),
        dispositivo: this.formInteraccion.dispositivo,
        reacciones_recibidas: Number(this.formInteraccion.reacciones_recibidas),
        etiquetas
      };
      this.authService.actualizarInteraccion(idEdit, payload).subscribe({
        next: (res) => {
          this.guardando.set(false);
          this.mensajeApi.set(`200 OK · Interacción ${res.id_interaccion} actualizada en las 5 tablas de Cassandra.`);
          this.cancelarEdicion();
          this.cargarDatos();
        },
        error: (err) => {
          this.guardando.set(false);
          this.mostrarErrorHttp(err);
        }
      });
    } else {
      const payload = {
        id_usuario: this.formInteraccion.id_usuario.trim(),
        canal: this.formInteraccion.canal,
        tipo: this.formInteraccion.tipo,
        tema: this.formInteraccion.tema,
        texto: this.formInteraccion.tipo === 'reaccion' ? null : this.formInteraccion.texto,
        sentimiento: Number(this.formInteraccion.sentimiento),
        dispositivo: this.formInteraccion.dispositivo,
        reacciones_recibidas: Number(this.formInteraccion.reacciones_recibidas),
        etiquetas
      };
      this.authService.crearInteraccion(payload).subscribe({
        next: (res) => {
          this.guardando.set(false);
          this.mensajeApi.set(`201 Created · Interacción ${res.id_interaccion} registrada para ${res.id_usuario} (estado: ${res.estado || 'null'}).`);
          this.formInteraccion.texto = '';
          this.cargarDatos();
        },
        error: (err) => {
          this.guardando.set(false);
          this.mostrarErrorHttp(err);
        }
      });
    }
  }

  eliminar(idInteraccion: string): void {
    this.mensajeApi.set(null);
    this.authService.eliminarInteraccion(idInteraccion).subscribe({
      next: (res) => {
        this.esErrorApi.set(false);
        this.mensajeApi.set(`200 OK · ${res.mensaje} (${res.id_interaccion}).`);
        this.cargarDatos();
      },
      error: (err) => {
        this.mostrarErrorHttp(err);
      }
    });
  }

  private mostrarErrorHttp(err: any): void {
    this.esErrorApi.set(true);
    const status = err?.status || 400;
    const detalle = err?.error?.errores ? ` (${err.error.errores.join(' ')})` : '';
    const msg = err?.error?.mensaje || 'Error al procesar la solicitud.';
    this.mensajeApi.set(`HTTP ${status}: ${msg}${detalle}`);
  }

  private dispararFlash(): void {
    this.flashKpi.set(false);
    setTimeout(() => this.flashKpi.set(true), 15);
  }

  cerrarSesion(): void {
    this.authService.logout();
  }
}
