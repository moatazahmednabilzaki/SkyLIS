import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  template: `
    <h1 class="pt">Dashboard</h1>
    <p class="sub">M05 · P05.1 — live KPIs arrive with the analytics projections slice.</p>
    <div class="kpis">
      <div class="kpi"><div class="v">—</div><div class="l">Visits today</div></div>
      <div class="kpi"><div class="v">—</div><div class="l">In process</div></div>
      <div class="kpi"><div class="v">—</div><div class="l">Awaiting validation</div></div>
      <div class="kpi"><div class="v">—</div><div class="l">Reported</div></div>
    </div>
    <div class="card">
      <h3>Start here</h3>
      <a class="btn" routerLink="/visits/new">＋ Register a visit</a>
      <a class="btn ghost" routerLink="/patients" style="margin-left:8px">Search patients</a>
    </div>
  `,
  styles: `
    .pt { font-size: 22px; font-weight: 700; color: var(--navy); margin-bottom: 4px; }
    .sub { font-size: 12px; color: var(--slate); margin-bottom: 16px; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin-bottom: 18px; }
    .kpi { background: #fff; border: 1px solid var(--line); border-radius: var(--radius); padding: 13px 15px; }
    .kpi .v { font-size: 24px; font-weight: 700; color: var(--blue); }
    .kpi .l { font-size: 11px; color: var(--slate); margin-top: 2px; }
  `,
})
export class DashboardComponent {}
