import { Component, inject, signal, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-folder-create',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './folder-create.component.html',
})
export class FolderCreateComponent {
  private api = inject(ApiService);

  folderCreated = output<void>();
  folderPath = '';
  loading = signal(false);
  message = signal('');
  success = signal(false);

  create() {
    const path = this.folderPath.trim();
    if (!path) return;
    this.loading.set(true);
    this.message.set('');
    this.api.createFolder(path).subscribe({
      next: () => {
        this.success.set(true);
        this.message.set(`✓ Mappen "${path}" skapades`);
        this.folderPath = '';
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

