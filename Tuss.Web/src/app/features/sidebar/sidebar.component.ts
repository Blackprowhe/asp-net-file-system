import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationService } from '../../core/services/navigation.service';
import { StoredFile } from '../../core/models/file.model';
import {
  HomeIconComponent, FolderIconComponent, FolderOpenIconComponent,
  ChevronRightIconComponent, ChevronDownIconComponent,
  FileIconComponent, ImageIconComponent, VideoIconComponent,
  CodeIconComponent, TextIconComponent, ArchiveIconComponent
} from '../../shared/icons/icons';

const IMAGE_EXTS  = new Set(['.jpg','.jpeg','.png','.gif','.webp','.svg','.avif','.bmp']);

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [
    CommonModule, HomeIconComponent, FolderIconComponent,
    FolderOpenIconComponent, ChevronRightIconComponent, ChevronDownIconComponent,
    FileIconComponent, ImageIconComponent
  ],
  templateUrl: './sidebar.component.html',
})
export class SidebarComponent {
  nav = inject(NavigationService);

  // Håller koll på vilka mappar som är expanderade manuellt
  expandedPaths = signal<Set<string>>(new Set<string>());

  /** Returnerar både mappar och filer som är direkta barn av en given sökväg */
  childEntries(parentPath: string) {
    const prefix = parentPath ? parentPath + '/' : '';
    return this.nav.allEntries().filter(e => {
      if (!e.name.startsWith(prefix)) return false;
      const rest = e.name.slice(prefix.length);
      return rest.length > 0 && !rest.includes('/');
    }).sort((a, b) => {
      // Mappar först, sen filer i bokstavsordning
      if (a.file !== b.file) return a.file ? 1 : -1;
      return a.name.localeCompare(b.name);
    });
  }

  isActive(path: string): boolean {
    return this.nav.currentPath() === path;
  }

  isExpanded(path: string): boolean {
    if (this.expandedPaths().has(path)) return true;
    const current = this.nav.currentPath();
    return current.startsWith(path + '/');
  }

  toggleExpand(path: string, event: MouseEvent) {
    event.stopPropagation();
    this.expandedPaths.update(set => {
      const next = new Set(set);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }

  navigate(target: StoredFile | string) {
    if (typeof target === 'string') {
      this.nav.navigateTo(target);
      return;
    }

    if (!target.file) {
      // Om det är en mapp, navigera och expandera
      this.expandedPaths.update(set => {
        const next = new Set(set);
        next.add(target.name);
        return next;
      });
      this.nav.navigateTo(target.name);
    } else {
      // Om det är en fil, vi kan antingen navigera till dess föräldramapp (redan där)
      // eller trigga en preview om det är en bild.
      // För tillfället låter vi bara navigeringen ske om det behövs.
      const lastSlash = target.name.lastIndexOf('/');
      const parentPath = lastSlash === -1 ? '' : target.name.slice(0, lastSlash);
      this.nav.navigateTo(parentPath);
    }
  }

  iconType(entry: StoredFile): string {
    if (!entry.file) return 'folder';
    const ext = (entry.name.slice(entry.name.lastIndexOf('.')) || '').toLowerCase();
    if (IMAGE_EXTS.has(ext))   return 'image';
    // ... kan lägga till fler här om vi vill ha färgade ikoner i sidebaren
    return 'file';
  }
}
