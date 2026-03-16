import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationService } from '../../core/services/navigation.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './sidebar.component.html',
})
export class SidebarComponent {
  nav = inject(NavigationService);

  /** Returnerar mappar som är direkta barn av en given sökväg */
  childFolders(parentPath: string) {
    const prefix = parentPath ? parentPath + '/' : '';
    return this.nav.folders().filter(f => {
      if (!f.name.startsWith(prefix)) return false;
      const rest = f.name.slice(prefix.length);
      return rest.length > 0 && !rest.includes('/');
    });
  }

  isActive(path: string): boolean {
    return this.nav.currentPath() === path;
  }

  isExpanded(path: string): boolean {
    const current = this.nav.currentPath();
    return current === path || current.startsWith(path + '/');
  }

  navigate(path: string) {
    this.nav.navigateTo(path);
  }
}

