import { DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config';
import { problemMessage } from '../../core/api.types';

interface PipelineStage { stage: string; count: number; }

interface Dashboard {
  day: string;
  visitsToday: number;
  statOpen: number;
  inProcess: number;
  awaitingTechnicalValidation: number;
  awaitingMedicalValidation: number;
  reportedToday: number;
  reservedSamplesPending: number;
  openCriticalValues: number;
  rejectionsToday: number;
  revenueToday: number;
  currency: string;
  medianRegisterToReportMinutes: number | null;
  pipeline: PipelineStage[];
}

/** P23.1 Executive Dashboard — live tenant KPIs (M23). */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DecimalPipe],
  template: `
    <h1 class="pt">Dashboard</h1>
    <p class="sub">M23 · P23.1 — live tenant KPIs. Figures reconcile to the same store the modules write.</p>

    @if (error()) { <div class="err">{{ error() }}</div> }
    @if (data(); as d) {
      @if (d.openCriticalValues > 0) {
        <div class="err">🚨 <b>{{ d.openCriticalValues }} critical value(s) awaiting read-back documentation</b>
          — <a routerLink="/critical">open the console →</a></div>
      }
      <div class="kpis">
        <div class="kpi"><div class="v navy">{{ d.visitsToday }}</div><div class="l">Visits today</div></div>
        <div class="kpi"><div class="v" [class.red]="d.statOpen > 0">{{ d.statOpen }}</div><div class="l">STAT open</div></div>
        <div class="kpi"><div class="v">{{ d.inProcess }}</div><div class="l">In process</div></div>
        <div class="kpi"><div class="v amber">{{ d.awaitingTechnicalValidation + d.awaitingMedicalValidation }}</div>
          <div class="l">Awaiting validation ({{ d.awaitingTechnicalValidation }} tech · {{ d.awaitingMedicalValidation }} med)</div></div>
        <div class="kpi"><div class="v green">{{ d.reportedToday }}</div><div class="l">Reported today</div></div>
        <div class="kpi"><div class="v">{{ d.reservedSamplesPending }}</div><div class="l">Reserved (condition)</div></div>
        <div class="kpi"><div class="v navy">{{ d.revenueToday }} {{ d.currency }}</div><div class="l">Payments today</div></div>
        <div class="kpi"><div class="v" [class.amber]="d.rejectionsToday > 0">{{ d.rejectionsToday }}</div><div class="l">Rejections today</div></div>
        <div class="kpi"><div class="v">{{ d.medianRegisterToReportMinutes === null ? '—' : (d.medianRegisterToReportMinutes | number: '1.0-0') + ' min' }}</div>
          <div class="l">Median register → report</div></div>
      </div>

      <div class="card">
        <h3>Today's pipeline — visits by current stage</h3>
        <div class="pipe">
          @for (stage of d.pipeline; track stage.stage) {
            <div class="stage">
              <div class="bar-wrap"><div class="bar" [style.height.%]="barHeight(stage.count)"></div></div>
              <div class="n">{{ stage.count }}</div>
              <div class="s">{{ stage.stage }}</div>
            </div>
          }
        </div>
      </div>
    } @else if (!error()) { <p class="sub">Loading KPIs…</p> }

    <div class="card">
      <h3>Quick actions</h3>
      <a class="btn" routerLink="/visits/new">＋ Register a visit</a>
      <a class="btn ghost" routerLink="/results" style="margin-left:8px">Enter results</a>
      <a class="btn ghost" routerLink="/reports" style="margin-left:8px">Reporting</a>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; margin-bottom: 18px; }
    .kpi { background: #fff; border: 1px solid var(--line); border-radius: var(--radius); padding: 13px 15px; }
    .kpi .v { font-size: 22px; font-weight: 700; color: var(--blue); }
    .kpi .v.navy { color: var(--navy); } .kpi .v.red { color: var(--red); }
    .kpi .v.green { color: var(--green); } .kpi .v.amber { color: var(--amber); }
    .kpi .l { font-size: 11px; color: var(--slate); margin-top: 2px; }
    .pipe { display: flex; gap: 18px; align-items: flex-end; padding: 8px 4px 0; }
    .stage { text-align: center; flex: 1; }
    .bar-wrap { height: 90px; display: flex; align-items: flex-end; justify-content: center; }
    .bar { width: 34px; background: var(--blue); border-radius: 6px 6px 0 0; min-height: 3px; }
    .n { font-weight: 700; color: var(--navy); margin-top: 4px; }
    .s { font-size: 10.5px; color: var(--slate); }
  `,
})
export class DashboardComponent implements OnInit {
  private readonly http = inject(HttpClient);

  readonly data = signal<Dashboard | null>(null);
  readonly error = signal<string | null>(null);
  private readonly maxStage = computed(() =>
    Math.max(1, ...(this.data()?.pipeline.map(p => p.count) ?? [1])));

  ngOnInit(): void {
    void this.load();
  }

  barHeight(count: number): number {
    return Math.max(3, (count / this.maxStage()) * 100);
  }

  async load(): Promise<void> {
    try {
      this.data.set(await firstValueFrom(this.http.get<Dashboard>(`${API_BASE_URL}/analytics/dashboard`)));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }
}
