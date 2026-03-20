import { Injectable, signal, computed } from '@angular/core';
import { StoredFile } from '../models/file.model';

/** Håller koll på aktuell mapp och hela filträdet. */
@Injectable({ providedIn: 'root' })
export class NavigationService {
  /** Aktuell mappväg, '' = root */
  readonly currentPath = signal('');

  /** Alla entries platt (filer + mappar) */
  readonly allEntries = signal<StoredFile[]>([]);

  /** Bara mappar (för sidebar-trädet) */
  readonly folders = computed(() =>
    this.allEntries().filter(e => !e.file)
  );

  /** Breadcrumb-segment: ['', 'mapp 1', 'mapp 1/sub'] */
  readonly breadcrumbs = computed(() => {
    const path = this.currentPath();
    if (!path) return [{ label: 'Mina filer', path: '' }];
    const parts = path.split('/');
    const crumbs = [{ label: 'Mina filer', path: '' }];
    for (let i = 0; i < parts.length; i++) {
      crumbs.push({
        label: parts[i],
        path: parts.slice(0, i + 1).join('/'),
      });
    }
    return crumbs;
  });

  /** Entries som är direkta barn av aktuell mapp */
  readonly currentEntries = computed(() => {
    const path = this.currentPath();
    const prefix = path ? path + '/' : '';
    return this.allEntries()
      .filter(e => {
        if (!e.name.startsWith(prefix)) return false;
        const rest = e.name.slice(prefix.length);
        // Direkt barn = inget snedstreck i resten
        return rest.length > 0 && !rest.includes('/');
      })
      .sort((a, b) => Number(a.file) - Number(b.file) || a.name.localeCompare(b.name));
  });

  /** Kort namn (sista segmentet) */
  shortName(fullPath: string): string {
    const idx = fullPath.lastIndexOf('/');
    return idx === -1 ? fullPath : fullPath.slice(idx + 1);
  }

  navigateTo(path: string) {
    this.currentPath.set(path);
  }
}

