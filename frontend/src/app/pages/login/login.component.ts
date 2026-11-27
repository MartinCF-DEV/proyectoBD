import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="login-wrap">
      <div class="lc">
        <div class="logo">C360</div>
        <h1>Community 360</h1>
        <p class="sub">Inicia sesión para entrar al panel</p>

        <form [formGroup]="loginForm" (ngSubmit)="onSubmit()" novalidate>
          <label for="correo">Correo electrónico o usuario</label>
          <input
            id="correo"
            type="text"
            formControlName="correo"
            placeholder="correo@ejemplo.com o usuario"
            autocomplete="username"
            [class.invalid]="isFieldInvalid('correo')"
          />
          <div class="err">
            @if (isFieldInvalid('correo')) {
              Ingresa tu usuario o correo electrónico.
            }
          </div>

          <label for="password">Contraseña</label>
          <div class="pw">
            <input
              id="password"
              [type]="mostrarPassword() ? 'text' : 'password'"
              formControlName="password"
              placeholder="Contraseña"
              autocomplete="current-password"
              [class.invalid]="isFieldInvalid('password')"
            />
            <button
              type="button"
              class="sh-btn"
              (click)="mostrarPassword.set(!mostrarPassword())"
            >
              {{ mostrarPassword() ? 'Ocultar' : 'Mostrar' }}
            </button>
          </div>
          <div class="err">
            @if (isFieldInvalid('password')) {
              Ingresa tu contraseña.
            } @else if (mensajeError()) {
              {{ mensajeError() }}
            }
          </div>

          <button type="submit" class="p go-btn" [disabled]="cargando()">
            {{ cargando() ? 'Verificando credenciales...' : 'Iniciar sesión' }}
          </button>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .login-wrap {
      min-height: 100vh;
      display: grid;
      place-items: center;
      padding: 20px;
      background:
        radial-gradient(circle at 20% 15%, #d4f1df 0, transparent 45%),
        radial-gradient(circle at 85% 85%, #cdeef0 0, transparent 40%),
        var(--bg);
    }
    .lc {
      background: #fff;
      border: 1px solid var(--bd);
      border-radius: 20px;
      box-shadow: var(--sh-lg);
      padding: 34px;
      width: 100%;
      max-width: 420px;
    }
    .logo {
      width: 46px;
      height: 46px;
      border-radius: 14px;
      background: linear-gradient(135deg, var(--g), var(--t));
      display: grid;
      place-items: center;
      color: #fff;
      font-weight: 800;
      font-size: 14px;
      margin-bottom: 14px;
    }
    h1 {
      margin: 0;
      font-size: 25px;
      font-weight: 700;
      color: var(--fg);
      letter-spacing: -0.02em;
    }
    .sub {
      margin: 4px 0 18px;
      color: var(--mut);
      font-size: 14px;
    }
    label {
      display: block;
      font-weight: 600;
      margin: 10px 0 6px;
      font-size: 13px;
      color: var(--fg);
    }
    input.invalid {
      border-color: var(--r);
    }
    .err {
      color: var(--r);
      font-size: 12px;
      margin-top: 4px;
      min-height: 17px;
    }
    .pw {
      position: relative;
    }
    .pw input {
      padding-right: 72px;
    }
    .sh-btn {
      position: absolute;
      right: 6px;
      top: 50%;
      transform: translateY(-50%);
      padding: 5px 10px;
      border: 0;
      border-radius: 8px;
      background: var(--gl);
      color: var(--gd);
      font-size: 12px;
      font-weight: 600;
    }
    .sh-btn:hover {
      background: #d1eedc;
    }
    .go-btn {
      width: 100%;
      margin-top: 12px;
      padding: 11px;
      font-size: 14px;
    }
  `]
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly cargando = signal(false);
  readonly mostrarPassword = signal(false);
  readonly mensajeError = signal<string | null>(null);

  readonly loginForm = this.fb.nonNullable.group({
    correo: ['', [Validators.required]],
    password: ['', [Validators.required]]
  });

  isFieldInvalid(field: 'correo' | 'password'): boolean {
    const control = this.loginForm.get(field);
    return !!(control && control.invalid && (control.dirty || control.touched));
  }

  onSubmit(): void {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.cargando.set(true);
    this.mensajeError.set(null);

    const credentials = this.loginForm.getRawValue();
    this.authService.login(credentials).subscribe({
      next: () => {
        this.cargando.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') || '/dashboard';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.cargando.set(false);
        if (err?.name === 'TimeoutError') {
          this.mensajeError.set('El servidor tardó demasiado en responder.');
          return;
        }
        const msg = err?.error?.mensaje || 'Credenciales incorrectas.';
        this.mensajeError.set(msg);
      }
    });
  }
}
