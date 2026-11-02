import { DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { LotsAdminApi } from '../../core/api/admin.api';
import { toApiError } from '../../core/api/problem-details';
import { ReportsApi } from '../../core/api/reports.api';
import { Lot } from '../parking/parking.models';
import { OccupancyReport, PerformanceReport } from '../../shared/models/report.models';
import { BarChart } from '../../shared/ui/bar-chart';
import { formatDuration, toDateInput } from '../../shared/ui/chart-math';
import { ErrorBanner, Loading } from '../../shared/ui/status';

/** Occupancy right now, plus revenue / stay / peak-hour figures for a chosen range. Computed by the API; this only shows them. */
@Component({
  selector: 'app-admin-dashboard',
  imports: [
    DecimalPipe,
    PercentPipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    BarChart,
    ErrorBanner,
    Loading,
  ],
  templateUrl: './admin-dashboard.html',
  styleUrl: './admin-dashboard.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboard {
  private readonly reports = inject(ReportsApi);
  private readonly lotsApi = inject(LotsAdminApi);

  protected readonly occupancy = signal<OccupancyReport | null>(null);
  protected readonly performance = signal<PerformanceReport | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly lots = signal<Lot[]>([]);

  protected readonly from = signal(toDateInput(new Date(Date.now() - 6 * 86_400_000)));
  protected readonly to = signal(toDateInput(new Date()));
  protected readonly lotId = signal('');

  protected readonly rangeInvalid = computed(() => {
    const f = this.from(),
      t = this.to();
    if (!f || !t) return 'Choose both dates.';
    if (t < f) return 'The end date cannot be before the start date.';
    const days = (Date.parse(t) - Date.parse(f)) / 86_400_000 + 1;
    return days > 92 ? 'Choose a range of at most 92 days.' : null;
  });

  protected readonly revenueBars = computed(() =>
    (this.performance()?.revenueByDay ?? []).map((d) => ({ label: d.date, value: d.amount })),
  );
  protected readonly peakBars = computed(() =>
    (this.performance()?.peakHours ?? []).map((h) => ({
      label: `${String(h.hour).padStart(2, '0')}:00`,
      value: h.checkIns,
    })),
  );
  protected readonly duration = formatDuration;

  constructor() {
    this.lotsApi.list('', 1, 100).subscribe({ next: (r) => this.lots.set(r.items) });
    this.load();
  }

  protected load(): void {
    if (this.rangeInvalid()) return;
    this.loading.set(true);
    this.error.set(null);
    let pending = 2;
    const done = () => --pending === 0 && this.loading.set(false);
    const fail = (e: unknown) => {
      this.error.set(toApiError(e).message);
      this.loading.set(false);
    };
    this.reports.occupancy().subscribe({
      next: (o) => {
        this.occupancy.set(o);
        done();
      },
      error: fail,
    });
    this.reports.performance(this.from(), this.to(), this.lotId()).subscribe({
      next: (p) => {
        this.performance.set(p);
        done();
      },
      error: fail,
    });
  }
}
