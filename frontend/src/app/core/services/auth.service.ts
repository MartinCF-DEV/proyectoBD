import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, of, tap, timeout } from 'rxjs';
import {
  ActualizarInteraccionPayload,
  AdminUser,
  CrearInteraccionPayload,
  DashboardResumen,
  InteraccionResumen,
  ListaInteraccionesResponse,
  LoginRequest,
  LoginResponse
} from '../models/auth.models';

const TOKEN_KEY = 'c360_jwt_token';
const EXP_KEY = 'c360_jwt_exp';
const USER_KEY = 'c360_admin_user';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiBase = this.resolveApiBase();

  private resolveApiBase(): string {
    const host = window.location.hostname;
    if (host === 'localhost' || host === '127.0.0.1') {
      return 'http://localhost:5000/api';
    }
    return '/api';
  }

  private readonly tokenSignal = signal<string | null>(this.getStoredToken());
  private readonly userSignal = signal<AdminUser | null>(this.getStoredUser());
  private readonly expSignal = signal<string | null>(localStorage.getItem(EXP_KEY));

  readonly currentUser = computed(() => this.userSignal());
  readonly token = computed(() => this.tokenSignal());
  readonly expiresAtUtc = computed(() => this.expSignal());
  readonly isAuthenticated = computed(() => {
    const token = this.tokenSignal();
    if (!token) return false;
    return !this.isTokenExpired();
  });

  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiBase}/auth/login`, credentials).pipe(
      timeout(8000),
      tap((res) => {
        localStorage.setItem(TOKEN_KEY, res.token);
        localStorage.setItem(EXP_KEY, res.expira_en_utc);
        localStorage.setItem(USER_KEY, JSON.stringify(res.usuario));
        this.tokenSignal.set(res.token);
        this.expSignal.set(res.expira_en_utc);
        this.userSignal.set(res.usuario);
      })
    );
  }

  verifySession(): Observable<AdminUser | null> {
    if (!this.isAuthenticated()) {
      this.clearLocalSession();
      return of(null);
    }
    return this.http.get<AdminUser>(`${this.apiBase}/auth/me`).pipe(
      tap((user) => {
        localStorage.setItem(USER_KEY, JSON.stringify(user));
        this.userSignal.set(user);
      }),
      catchError(() => {
        this.clearLocalSession();
        return of(null);
      })
    );
  }

  logout(): void {
    const currentToken = this.tokenSignal();
    if (currentToken) {
      this.http.post(`${this.apiBase}/auth/logout`, {})
        .pipe(
          catchError(() => of(null)),
          finalize(() => {
            this.clearLocalSession();
            this.router.navigate(['/login']);
          })
        )
        .subscribe();
    } else {
      this.clearLocalSession();
      this.router.navigate(['/login']);
    }
  }

  getDashboardResumen(): Observable<DashboardResumen> {
    return this.http.get<DashboardResumen>(`${this.apiBase}/dashboard/resumen`);
  }

  getInteracciones(filtros: {
    id_usuario?: string;
    tema?: string;
    canal?: string;
    estado?: string;
    etiqueta?: string;
    limite?: number;
  }): Observable<ListaInteraccionesResponse> {
    let params = new HttpParams();
    if (filtros.id_usuario) params = params.set('id_usuario', filtros.id_usuario);
    if (filtros.tema) params = params.set('tema', filtros.tema);
    if (filtros.canal) params = params.set('canal', filtros.canal);
    if (filtros.estado) params = params.set('estado', filtros.estado);
    if (filtros.etiqueta) params = params.set('etiqueta', filtros.etiqueta);
    if (filtros.limite) params = params.set('limite', filtros.limite);

    return this.http.get<ListaInteraccionesResponse>(`${this.apiBase}/interacciones`, { params });
  }

  crearInteraccion(payload: CrearInteraccionPayload): Observable<InteraccionResumen> {
    return this.http.post<InteraccionResumen>(`${this.apiBase}/interacciones`, payload);
  }

  actualizarInteraccion(id: string, payload: ActualizarInteraccionPayload): Observable<InteraccionResumen> {
    return this.http.put<InteraccionResumen>(`${this.apiBase}/interacciones/${encodeURIComponent(id)}`, payload);
  }

  eliminarInteraccion(id: string): Observable<{ id_interaccion: string; mensaje: string }> {
    return this.http.delete<{ id_interaccion: string; mensaje: string }>(`${this.apiBase}/interacciones/${encodeURIComponent(id)}`);
  }

  clearLocalSession(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(EXP_KEY);
    localStorage.removeItem(USER_KEY);
    this.tokenSignal.set(null);
    this.expSignal.set(null);
    this.userSignal.set(null);
  }

  private isTokenExpired(): boolean {
    const exp = this.expSignal();
    if (!exp) return false;
    const expiresTime = new Date(exp).getTime();
    return Number.isNaN(expiresTime) || Date.now() >= expiresTime;
  }

  private getStoredToken(): string | null {
    const token = localStorage.getItem(TOKEN_KEY);
    const exp = localStorage.getItem(EXP_KEY);
    if (!token) return null;
    if (exp && Date.now() >= new Date(exp).getTime()) {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(EXP_KEY);
      localStorage.removeItem(USER_KEY);
      return null;
    }
    return token;
  }

  private getStoredUser(): AdminUser | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as AdminUser;
    } catch {
      return null;
    }
  }
}
