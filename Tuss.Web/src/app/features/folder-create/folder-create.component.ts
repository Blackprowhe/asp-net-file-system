import { Component, inject, signal, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { NavigationService } from '../../core/services/navigation.service';

@Component({
  selector: 'app-folder-create',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './folder-create.component.html',
})
export class FolderCreateComponent {
  private api = inject(ApiService);
  private nav = inject(NavigationService);

  folderCreated = output<void>();
  folderName = '';
  loading = signal(false);
  message = signal('');
  success = signal(false);

  get pathPrefix(): string {
    const p = this.nav.currentPath();
    return p ? p + '/' : '';
  }

  create() {
    const name = this.folderName.trim();
    if (!name) return;
    const fullPath = this.pathPrefix + name;
    this.loading.set(true);
    this.message.set('');
    this.api.createFolder(fullPath).subscribe({
      next: () => {
        this.success.set(true);
        this.message.set(`✓ Mappen "${name}" skapades`);
        this.folderName = '';
        this.loading.set(false);
        this.folderCreated.emit();
      },
      error: (e) => {
        this.success.set(false);
        this.message.set(e.status === 409 ? `Mappen finns redan.` : `Fel: ${e.status}`);
        this.loading.set(false);
      },
    });
  }
}
