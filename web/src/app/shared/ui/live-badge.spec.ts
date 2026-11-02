import { TestBed } from '@angular/core/testing';
import { LiveBadge } from './live-badge';

describe('LiveBadge', () => {
  const render = (mode: 'live' | 'reconnecting' | 'polling') => {
    const fixture = TestBed.createComponent(LiveBadge);
    fixture.componentRef.setInput('mode', mode);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it.each([
    ['live', 'Live'],
    ['reconnecting', 'Reconnecting'],
    ['polling', 'Refreshes every 30 seconds'],
  ] as const)('says %s in words, so colour is never the only signal', (mode, text) => {
    const el = render(mode);
    expect(el.textContent).toContain(text);
    expect(el.querySelector('.badge')?.getAttribute('data-mode')).toBe(mode);
    expect(el.querySelector('mat-icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('is announced politely to screen readers when it changes', () => {
    expect(render('live').querySelector('.badge')?.getAttribute('role')).toBe('status');
  });

  it('is honest that without the live channel the screen only refreshes periodically', () => {
    expect(render('polling').textContent).not.toMatch(/live/i);
  });
});
