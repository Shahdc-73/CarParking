import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { niceMax } from './chart-math';

export interface Bar {
  label: string;
  value: number;
}

/**
 * A small dependency-free bar chart. The picture is decorative for assistive technology (aria-hidden); the same numbers are
 * available as a real table inside the <details>, so nothing is only visible, and nothing depends on colour.
 */
@Component({
  selector: 'app-bar-chart',
  template: `
    <figure>
      <figcaption>{{ title() }}</figcaption>
      <svg viewBox="0 0 400 160" preserveAspectRatio="none" aria-hidden="true" class="plot">
        @for (tick of ticks(); track tick.y) {
          <line class="grid" x1="0" x2="400" [attr.y1]="tick.y" [attr.y2]="tick.y" />
        }
        @for (b of shapes(); track $index) {
          <rect
            class="bar"
            [attr.x]="b.x"
            [attr.y]="b.y"
            [attr.width]="b.w"
            [attr.height]="b.h"
            rx="2"
          >
            <title>{{ b.text }}</title>
          </rect>
        }
      </svg>
      <p class="axis">
        <span>{{ bars()[0]?.label }}</span
        ><span>max {{ top() }}</span
        ><span>{{ bars().at(-1)?.label }}</span>
      </p>
      <details>
        <summary>View as table</summary>
        <table>
          <caption class="visually-hidden">
            {{
              title()
            }}
          </caption>
          <thead>
            <tr>
              <th scope="col">{{ labelHeader() }}</th>
              <th scope="col">{{ valueHeader() }}</th>
            </tr>
          </thead>
          <tbody>
            @for (b of bars(); track $index) {
              <tr>
                <th scope="row">{{ b.label }}</th>
                <td>{{ b.value }}</td>
              </tr>
            }
          </tbody>
        </table>
      </details>
    </figure>
  `,
  styles: `
    figure {
      margin: 0;
    }
    figcaption {
      font: var(--mat-sys-title-small);
      margin-bottom: 0.5rem;
    }
    .plot {
      width: 100%;
      height: 10rem;
      display: block;
    }
    .grid {
      stroke: var(--mat-sys-outline-variant);
      stroke-width: 1;
      vector-effect: non-scaling-stroke;
    }
    .bar {
      fill: var(--mat-sys-primary);
    }
    .axis {
      display: flex;
      justify-content: space-between;
      margin: 0.25rem 0;
      font: var(--mat-sys-label-small);
      color: var(--mat-sys-on-surface-variant);
    }
    summary {
      cursor: pointer;
      font: var(--mat-sys-label-large);
      padding: 0.25rem 0;
    }
    table {
      border-collapse: collapse;
      margin-top: 0.5rem;
    }
    th,
    td {
      padding: 0.25rem 0.75rem;
      text-align: left;
      font: var(--mat-sys-body-small);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BarChart {
  readonly title = input.required<string>();
  readonly bars = input.required<Bar[]>();
  readonly labelHeader = input('Period');
  readonly valueHeader = input('Value');

  protected readonly top = computed(() => niceMax(Math.max(0, ...this.bars().map((b) => b.value))));

  protected readonly ticks = computed(() =>
    [0, 0.5, 1].map((f) => ({ y: 160 - f * 160 + (f === 0 ? -0.5 : f === 1 ? 0.5 : 0) })),
  );

  protected readonly shapes = computed(() => {
    const bars = this.bars();
    const slot = 400 / Math.max(1, bars.length);
    const top = this.top();
    return bars.map((b, i) => {
      const h = (b.value / top) * 160;
      return {
        x: i * slot + slot * 0.1,
        w: slot * 0.8,
        y: 160 - h,
        h,
        text: `${b.label}: ${b.value}`,
      };
    });
  });
}
