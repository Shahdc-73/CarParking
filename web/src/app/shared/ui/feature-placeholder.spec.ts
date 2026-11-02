import { TestBed } from '@angular/core/testing';
import { FeaturePlaceholder } from './feature-placeholder';

describe('FeaturePlaceholder', () => {
  it('shows each planned feature as a tile with a "Coming soon" badge, under a page heading', async () => {
    await TestBed.configureTestingModule({ imports: [FeaturePlaceholder] }).compileComponents();
    const fixture = TestBed.createComponent(FeaturePlaceholder);
    fixture.componentRef.setInput('title', 'Gate');
    fixture.componentRef.setInput('intro', 'Check vehicles in and out.');
    fixture.componentRef.setInput('items', [
      { icon: 'login', title: 'Check in', description: 'Enter a plate.' },
      { icon: 'logout', title: 'Check out', description: 'Take payment.' },
    ]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent).toBe('Gate');
    expect(el.textContent).toContain('Check vehicles in and out.');
    const tiles = el.querySelectorAll('.tile');
    expect(tiles.length).toBe(2);
    expect(tiles[0].querySelector('h2')?.textContent).toBe('Check in');
    expect(tiles[0].textContent).toContain('Coming soon');
    expect(tiles[0].querySelector('mat-icon')?.getAttribute('aria-hidden')).toBe('true');
  });
});
