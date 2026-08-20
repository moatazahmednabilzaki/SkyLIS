import { Component, HostListener, inject, signal, viewChild, ElementRef } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './core/auth.service';
import { API_BASE_URL } from './core/config';

interface SearchHit {
  kind: string;
  id: string;
  title: string;
  subtitle: string;
  navigateId: string | null;
}

interface GlobalSearch {
  patients: SearchHit[];
  visits: SearchHit[];
  samples: SearchHit[];
  invoices: SearchHit[];
  tests: SearchHit[];
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ReactiveFormsModule],
  template: `
    @if (auth.isAuthenticated()) {
      <div class="topbar">
        <div class="logo">
          <svg width="29" height="29" viewBox="0 0 56 56" fill="none" aria-hidden="true">
            <rect x="2" y="2" width="52" height="52" rx="14" fill="#e7f4fd"/>
            <path d="M21 13 h9" stroke="#0284c7" stroke-width="3" stroke-linecap="round"/>
            <path d="M22.5 13 v15 a3.5 3.5 0 0 0 7 0 V13" stroke="#0284c7" stroke-width="3"
                  stroke-linecap="round" stroke-linejoin="round"/>
            <path d="M22.5 22 v6 a3.5 3.5 0 0 0 7 0 v-6 Z" fill="#0284c7"/>
            <circle cx="36.5" cy="17" r="4.5" fill="#0284c7"/>
            <path d="M15 38 h26" stroke="#0284c7" stroke-width="3.5" stroke-linecap="round"/>
            <path d="M19 45 h13" stroke="#74bce2" stroke-width="3" stroke-linecap="round"/>
          </svg>
          <span class="wm">Sky</span><span class="lis">LIS</span>
        </div>
        <div class="gsearch">
          <input #searchBox [formControl]="searchTerm" placeholder="Search patients · visits · samples · invoices (Ctrl+K)"
                 (keyup.enter)="runSearch()" (keyup.escape)="clearSearch()">
          @if (searchResult(); as r) {
            <div class="gresults">
              @for (hit of r.patients; track hit.id) {
                <a (click)="go('/patients/' + hit.navigateId)">👤 <b>{{ hit.title }}</b> <span class="ghint">{{ hit.subtitle }}</span></a>
              }
              @for (hit of r.visits; track hit.id) {
                <a (click)="go('/visits/' + hit.navigateId)">📝 <b class="mono">{{ hit.title }}</b> <span class="ghint">{{ hit.subtitle }}</span></a>
              }
              @for (hit of r.samples; track hit.id) {
                <a (click)="go('/visits/' + hit.navigateId)">🧪 <b class="mono">{{ hit.title }}</b> <span class="ghint">{{ hit.subtitle }}</span></a>
              }
              @for (hit of r.invoices; track hit.id) {
                <a (click)="go('/visits/' + hit.navigateId)">💳 <b class="mono">{{ hit.title }}</b> <span class="ghint">{{ hit.subtitle }}</span></a>
              }
              @for (hit of r.tests; track hit.id) {
                <a><b class="mono">{{ hit.title }}</b> <span class="ghint">{{ hit.subtitle }}</span></a>
              }
              @if (r.patients.length + r.visits.length + r.samples.length + r.invoices.length + r.tests.length === 0) {
                <a><span class="ghint">No matches.</span></a>
              }
            </div>
          }
        </div>
        <span class="ctx">{{ auth.user()?.fullName ?? 'signed in' }} · {{ auth.user()?.roles?.join(', ') }}</span>
        <span class="spacer"></span>
        <button class="btn ghost sm" (click)="changePassword()" title="Change my password (§4.3)">🔑</button>
        <button class="btn ghost sm" (click)="logout()">Sign out</button>
      </div>
      <nav class="sidebar">
        <div class="group">FRONT OFFICE</div>
        <a routerLink="/dashboard" routerLinkActive="on">📊 Dashboard</a>
        <a routerLink="/visits/new" routerLinkActive="on">📝 Visit Registration</a>
        <a routerLink="/reception" routerLinkActive="on">🛎️ Reception Worklist</a>
        <a routerLink="/patients" routerLinkActive="on">👤 Patients</a>
        <a routerLink="/cashier" routerLinkActive="on">💰 Cashier &amp; Day Close</a>
        <div class="group">LABORATORY</div>
        <a routerLink="/phlebotomist" routerLinkActive="on">💉 Phlebotomist Worklist</a>
        <a routerLink="/results" routerLinkActive="on">🧪 Results Entry</a>
        <a routerLink="/validation" routerLinkActive="on">✅ Validation &amp; Sign-Out</a>
        <a routerLink="/critical" routerLinkActive="on">🚨 Critical Values</a>
        <a routerLink="/reports" routerLinkActive="on">📄 Reporting</a>
        <div class="group">QUALITY</div>
        <a routerLink="/analytics" routerLinkActive="on">📈 Analytics</a>
        <a routerLink="/audit" routerLinkActive="on">🔗 Audit Trail</a>
        <div class="group">ADMINISTRATION</div>
        <a routerLink="/users" routerLinkActive="on">🔐 Users &amp; Roles</a>
        <a routerLink="/branches" routerLinkActive="on">🏢 Branches</a>
        <a routerLink="/catalog" routerLinkActive="on">🧪 Test Catalogue</a>
        <a routerLink="/setup" routerLinkActive="on">🧭 Lab Setup</a>
        <div class="foot">Client Portal · v1.0 · SRS Rev 2.0</div>
      </nav>
      <main class="main"><router-outlet /></main>
    } @else {
      <router-outlet />
    }
  `,
  styles: `
    .topbar {
      position: fixed; top: 0; left: 0; right: 0; height: 56px; background: #fff;
      border-bottom: 1px solid var(--line); display: flex; align-items: center;
      gap: 14px; padding: 0 18px; z-index: 20;
    }
    .logo { display: flex; align-items: center; gap: 9px; }
    .wm { font-size: 19px; font-weight: 700; letter-spacing: -.02em; color: var(--navy); }
    .lis {
      font-size: 9px; font-weight: 600; letter-spacing: .18em; color: var(--blue);
      border: 1px solid #bfe1f6; border-radius: 4px; padding: 1px 5px;
    }
    .ctx { font-size: 11px; color: var(--slate); background: #eef3f9; border-radius: 6px; padding: 5px 11px; }
    .spacer { flex: 1; }
    .gsearch { position: relative; flex: 0 1 420px; margin-left: 14px; }
    .gsearch input {
      width: 100%; font-size: 12px; border: 1px solid var(--line); border-radius: 8px;
      padding: 7px 11px; background: #f6f9fc;
    }
    .gresults {
      position: absolute; top: 36px; left: 0; right: 0; background: #fff; z-index: 50;
      border: 1px solid var(--line); border-radius: 10px; box-shadow: 0 10px 30px rgba(16,29,44,.14);
      max-height: 380px; overflow: auto; padding: 4px;
    }
    .gresults a {
      display: block; padding: 7px 10px; font-size: 12px; color: var(--navy);
      border-radius: 6px; cursor: pointer; text-decoration: none;
    }
    .gresults a:hover { background: #eef7fd; }
    .ghint { color: var(--slate); font-size: 11px; margin-left: 6px; }
    .sidebar {
      position: fixed; top: 56px; left: 0; bottom: 0; width: 208px;
      background: var(--navy); padding: 14px 0; display: flex; flex-direction: column;
    }
    .group { font-size: 9.5px; font-weight: 700; letter-spacing: .14em; color: #5f7186; padding: 14px 18px 6px; }
    .sidebar a {
      display: block; padding: 9px 18px; color: #c6d4e2; font-size: 12.5px;
      text-decoration: none; border-left: 3px solid transparent;
    }
    .sidebar a:hover { background: rgba(255, 255, 255, .05); }
    .sidebar a.on {
      background: var(--blue); color: #fff; border-left-color: #7dd3fc;
      border-radius: 0 8px 8px 0; margin-right: 12px; font-weight: 600;
    }
    .foot { margin-top: auto; padding: 16px 18px; font-size: 10px; color: #5f7186; border-top: 1px solid #1e2c44; }
    .main { margin: 56px 0 0 208px; padding: 22px 26px 60px; min-height: calc(100vh - 56px); }
  `,
})
export class AppComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly searchTerm = this.fb.nonNullable.control('');
  readonly searchResult = signal<GlobalSearch | null>(null);
  readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');

  /** FR-SYS-008: Ctrl+K focuses the global search from anywhere. */
  @HostListener('window:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.searchBox()?.nativeElement.focus();
    }
  }

  async runSearch(): Promise<void> {
    const term = this.searchTerm.value.trim();
    if (term.length < 2) { this.searchResult.set(null); return; }
    try {
      const params = new HttpParams().set('term', term);
      this.searchResult.set(await firstValueFrom(
        this.http.get<GlobalSearch>(`${API_BASE_URL}/search`, { params })));
    } catch {
      this.searchResult.set(null);
    }
  }

  clearSearch(): void {
    this.searchResult.set(null);
    this.searchTerm.setValue('');
  }

  go(path: string): void {
    this.clearSearch();
    void this.router.navigateByUrl(path);
  }

  async changePassword(): Promise<void> {
    const current = window.prompt('Current password:');
    if (!current) return;
    const next = window.prompt('New password (≥ 12 characters):');
    if (!next) return;
    try {
      await firstValueFrom(this.http.post(`${API_BASE_URL}/users/me/change-password`, {
        currentPassword: current, newPassword: next,
      }));
      window.alert('Password changed ✓');
    } catch {
      window.alert('Password change failed — check the current password and the 12-character policy.');
    }
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}
