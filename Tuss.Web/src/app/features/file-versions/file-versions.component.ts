import { Component, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FileVersion } from '../../core/models/file.model';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-file-versions',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './file-versions.component.html',
})
export class FileVersionsComponent {
  private api = inject(ApiService);

  fileName = input.required<string>();
  versions = input.required<FileVersion[]>();
  closed = output<void>();
  restored = output<void>();

  loading = signal(false);
  restoring = signal(false);
  message = signal('');
  success = signal(false);

  downloadUrl(version: number): string {
    return `/api/files/${encodeURIComponent(this.fileName())}/versions/${version}`;
  }

  restore(version: number) {
    this.restoring.set(true);
    this.message.set('');
    this.api.restoreVersion(this.fileName(), version).subscribe({
      next: () => {
        this.success.set(true);
        this.message.set(`✓ Återställd till v${version}`);
        this.restoring.set(false);
        this.restored.emit();
      },
      error: (e) => {
        this.success.set(false);
        this.message.set(`Fel: ${e.status}`);
        this.restoring.set(false);
      },
    });
  }

  formatBytes(b: number): string {
    if (b < 1024) return `${b} B`;
    if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
    return `${(b / (1024 * 1024)).toFixed(1)} MB`;
  }
}

