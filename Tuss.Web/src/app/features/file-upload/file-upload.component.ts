import { Component, inject, signal, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { NavigationService } from '../../core/services/navigation.service';

@Component({
  selector: 'app-file-upload',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './file-upload.component.html',
})
export class FileUploadComponent {
  private api = inject(ApiService);
  private nav = inject(NavigationService);

  uploadDone = output<void>();

  selectedFile = signal<File | null>(null);
  targetName = '';
  loading = signal(false);
  message = signal('');
  success = signal(false);

  /** Visar aktuell mapp som prefix i UI */
  get pathPrefix(): string {
    const p = this.nav.currentPath();
    return p ? p + '/' : '';
  }

  onFileChange(files: FileList | null) {
    const f = files?.[0] ?? null;
    this.selectedFile.set(f);
    this.targetName = f?.name ?? '';
    this.message.set('');
  }

  upload(method: 'post' | 'put') {
    const file = this.selectedFile();
    const name = this.targetName.trim();
    if (!file || !name) return;

    const fullPath = this.pathPrefix + name;
    this.loading.set(true);
    this.message.set('');

    const call = method === 'post'
      ? this.api.uploadFile(fullPath, file)
      : this.api.replaceFile(fullPath, file);

    call.subscribe({
      next: () => {
        this.success.set(true);
        this.message.set(`✓ "${name}" uppladdad`);
        this.clear();
        this.loading.set(false);
        this.uploadDone.emit();
      },
      error: (e) => {
        this.success.set(false);
        this.message.set(e.status === 409 ? `Filen finns redan (använd PUT för ny version).` : `Fel: ${e.status}`);
        this.loading.set(false);
      },
    });
  }

  clear() {
    this.selectedFile.set(null);
    this.targetName = '';
  }
}
