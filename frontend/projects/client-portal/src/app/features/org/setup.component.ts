import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';

interface SetupStatus {
  branches: number; departments: number; sampleTypes: number; activeTests: number;
  panels: number; users: number; settings: number; catalogReady: boolean; teamReady: boolean;
}

interface Setting { key: string; value: string; updatedAtUtc: string; }
interface ImportResult { created: number; skipped: number; errors: string[]; }

/**
 * P03.1 Setup Wizard + FR-SYS-004 settings + FR-SYS-009 catalog CSV import/export.
 * The checklist reads live counts; each step links to the page that completes it.
 */
@Component({
  selector: 'app-setup',
  imports: [ReactiveFormsModule, DatePipe, RouterLink],
  template: `
    <h1 class="pt">Lab Setup</h1>
    <p class="sub">M03 · P03.1 — the guided path from a fresh tenant to a working laboratory.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (info()) { <div class="note">{{ info() }}</div> }

    @if (status(); as s) {
      <div class="card">
        <h3>Checklist</h3>
        <table class="t">
          <tr>
            <td>{{ s.branches > 0 ? '✅' : '⬜' }} <b>Branches</b> <span class="hint">({{ s.branches }} active, {{ s.departments }} departments — the MAIN branch ships with provisioning)</span></td>
            <td style="text-align:right"><a class="btn sm ghost" routerLink="/branches">Open →</a></td>
          </tr>
          <tr>
            <td>{{ s.sampleTypes > 0 ? '✅' : '⬜' }} <b>Sample taxonomy</b> <span class="hint">({{ s.sampleTypes }} types — seeded from the country pack, FR-TEN-040)</span></td>
            <td></td>
          </tr>
          <tr>
            <td>{{ s.activeTests > 0 ? '✅' : '⬜' }} <b>Test catalogue</b> <span class="hint">({{ s.activeTests }} active tests — create &amp; approve them, or import below)</span></td>
            <td style="text-align:right"><a class="btn sm ghost" routerLink="/catalog">Open →</a></td>
          </tr>
          <tr>
            <td>{{ s.panels > 0 ? '✅' : '⬜' }} <b>Panels / profiles</b> <span class="hint">({{ s.panels }} bundles — P03.5)</span></td>
            <td></td>
          </tr>
          <tr>
            <td>{{ s.teamReady ? '✅' : '⬜' }} <b>Team</b> <span class="hint">({{ s.users }} accounts — one personal login per staff member)</span></td>
            <td style="text-align:right"><a class="btn sm ghost" routerLink="/users">Open →</a></td>
          </tr>
          <tr>
            <td>{{ s.settings > 0 ? '✅' : '⬜' }} <b>Settings</b> <span class="hint">({{ s.settings }} values — report branding, rejection vocabulary)</span></td>
            <td></td>
          </tr>
        </table>
      </div>
    }

    <div class="card">
      <h3>Settings (FR-SYS-004)</h3>
      <table class="t">
        <tr><th>Key</th><th>Value</th><th>Updated</th></tr>
        @for (s of settings(); track s.key) {
          <tr>
            <td class="mono">{{ s.key }}</td>
            <td>{{ s.value }}</td>
            <td>{{ s.updatedAtUtc | date: 'MM-dd HH:mm' }}</td>
          </tr>
        }
        @if (settings().length === 0) { <tr><td colspan="3" class="hint">No settings yet — the defaults apply.</td></tr> }
      </table>
      <form (ngSubmit)="setSetting()" style="margin-top:8px">
        <div class="f-row">
          <div class="f" style="flex:0 0 260px">
            <label for="sk">KEY</label>
            <input id="sk" class="mono" [formControl]="settingKey" placeholder="report.footerNote">
          </div>
          <div class="f" style="flex:2">
            <label for="sv">VALUE</label>
            <input id="sv" [formControl]="settingValue">
          </div>
          <div class="f" style="flex:0 0 auto; align-self:flex-end">
            <button class="btn" type="submit" [disabled]="settingKey.invalid || settingValue.invalid || busy()">Save</button>
          </div>
        </div>
      </form>
      <p class="hint">Known keys: report.headerNameOverride · report.footerNote · report.footerNoteAr · rejection.reasons (comma-separated coded vocabulary)</p>
    </div>

    <div class="card">
      <h3>Catalog CSV import / export (FR-SYS-009)</h3>
      <button class="btn ghost sm" (click)="exportCsv()">Download catalogue CSV</button>
      <div class="f-row" style="margin-top:10px">
        <div class="f" style="flex:2">
          <label for="csv">IMPORT CSV (Code,Name,Department,SampleTypeName,ConditionName,Price,Currency)</label>
          <textarea id="csv" rows="5" class="mono" [formControl]="csv"
                    placeholder="Code,Name,Department,SampleTypeName,ConditionName,Price,Currency&#10;NA,Sodium,Chemistry,Serum,Random,60,EGP"></textarea>
        </div>
        <div class="f" style="flex:0 0 auto; align-self:flex-end">
          <button class="btn green" (click)="importCsv()" [disabled]="csv.invalid || busy()">Import</button>
        </div>
      </div>
      @if (importResult(); as r) {
        <div class="note" style="margin-top:8px">
          Imported: {{ r.created }} created, {{ r.skipped }} skipped (existing codes).
          @for (e of r.errors; track e) { <div class="hint">⚠ {{ e }}</div> }
        </div>
      }
      <p class="hint">Imported tests arrive as Draft and walk the normal review → approval flow (P03.3).</p>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    textarea { width: 100%; border: 1px solid var(--line); border-radius: 8px; padding: 8px 10px; font-size: 12px; }
  `,
})
export class SetupComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly status = signal<SetupStatus | null>(null);
  readonly settings = signal<Setting[]>([]);
  readonly importResult = signal<ImportResult | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  readonly settingKey = this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(80)]);
  readonly settingValue = this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(2000)]);
  readonly csv = this.fb.nonNullable.control('', Validators.required);

  ngOnInit(): void {
    void this.reload();
  }

  async reload(): Promise<void> {
    this.error.set(null);
    try {
      const [status, settings] = await Promise.all([
        firstValueFrom(this.http.get<SetupStatus>(`${API_BASE_URL}/org/setup-status`)),
        firstValueFrom(this.http.get<Setting[]>(`${API_BASE_URL}/org/settings`)),
      ]);
      this.status.set(status);
      this.settings.set(settings);
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  async setSetting(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.put(`${API_BASE_URL}/org/settings`, {
        key: this.settingKey.value, value: this.settingValue.value,
      }));
      this.settingKey.reset();
      this.settingValue.reset();
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  async exportCsv(): Promise<void> {
    const csv = await firstValueFrom(this.http.get(
      `${API_BASE_URL}/catalog/tests/export.csv`, { responseType: 'text' }));
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'skylis-catalogue.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  async importCsv(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.importResult.set(await firstValueFrom(this.http.post<ImportResult>(
        `${API_BASE_URL}/catalog/tests/import`, { csv: this.csv.value })));
      this.csv.reset();
      await this.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
