import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/auth.service';

interface PackCondition {
  name: string;
  delayMinutes: number | null;
  compatibilityGroup: string;
}

interface PackSampleType {
  name: string;
  containerName: string;
  conditions: PackCondition[];
}

interface CountryPack {
  id: string;
  countryCode: string;
  name: string;
  currency: string;
  version: number;
  updatedAtUtc: string;
  sampleTypes: PackSampleType[];
}

/**
 * P01.4 Country Packs: the defaults each new tenant is seeded with at provisioning
 * (FR-TEN-040). Packs apply at provisioning time only; existing tenants are untouched.
 */
@Component({
  selector: 'app-country-packs',
  imports: [DatePipe],
  template: `
    <h1 class="pt">Country Packs</h1>
    <p class="sub">M01 · P01.4 — provisioning seeds each new tenant's sample taxonomy from its country's pack.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }

    @for (p of packs(); track p.id) {
      <div class="card">
        <div style="display:flex; align-items:center; gap:10px; margin-bottom:8px">
          <h3 style="margin:0">{{ p.name }}</h3>
          <span class="chip c-blue mono">{{ p.countryCode }}</span>
          <span class="chip mono">{{ p.currency }}</span>
          <span class="chip c-green">v{{ p.version }}</span>
          <span style="flex:1"></span>
          <span class="hint">updated {{ p.updatedAtUtc | date: 'yyyy-MM-dd HH:mm' }}</span>
        </div>
        <table class="t">
          <tr><th>Sample type</th><th>Container</th><th>Conditions</th></tr>
          @for (s of p.sampleTypes; track s.name) {
            <tr>
              <td><b>{{ s.name }}</b></td>
              <td>{{ s.containerName }}</td>
              <td>
                @for (c of s.conditions; track c.name) {
                  <span class="chip" [class.c-amber]="c.delayMinutes !== null" style="margin-right:4px">
                    {{ c.name }}{{ c.delayMinutes !== null ? ' (+' + c.delayMinutes + 'min)' : '' }}
                    · {{ c.compatibilityGroup }}
                  </span>
                }
              </td>
            </tr>
          }
        </table>
      </div>
    }
    @if (packs().length === 0 && !error()) { <p class="sub">Loading…</p> }
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: #fff; margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
  `,
})
export class CountryPacksComponent implements OnInit {
  private readonly http = inject(HttpClient);

  readonly packs = signal<CountryPack[]>([]);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.packs.set(await firstValueFrom(
        this.http.get<CountryPack[]>(`${API_BASE_URL}/platform/country-packs`)));
    } catch {
      this.error.set('Could not load country packs. Is the API running?');
    }
  }
}
