import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { StoredFile, FileVersion } from '../../core/models/file.model';
import { FileVersionsComponent } from '../file-versions/file-versions.component';

@Component({
  selector: 'app-file-browser',
  standalone: true,
  imports: [CommonModule, FileVersionsComponent],
  templateUrl: './file-browser.component.html',
})
export class FileBrowserComponent implements OnInit {
  private api = inject(ApiService);

  entries = signal<StoredFile[]>([]);
  loading = signal(false);

  versionsFor = signal<string | null>(null);
  currentVersions = signal<FileVersion[]>([]);
  versionsLoading = signal(false);

  actionMessage = signal('');
  actionSuccess = signal(false);

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.api.getFiles().subscribe({
      next: (map) => {
        this.entries.set(
          Object.entries(map).map(([name, meta]) => ({
            name,
            created: meta.created,
            changed: meta.changed,
            file: meta.file,
            bytes: meta.bytes,
            extension: meta.extension,
            currentVersion: meta.currentVersion,
          })).sort((a, b) => Number(a.file) - Number(b.file) || a.name.localeCompare(b.name))
        );
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  downloadUrl(name: string): string {
    return `/api/files/${name.split('/').map(encodeURIComponent).join('/')}`;
  }

  showVersions(entry: StoredFile) {
    this.versionsFor.set(entry.name);
    this.currentVersions.set([]);
    this.api.getVersions(entry.name).subscribe({
      next: (v) => this.currentVersions.set(v),
    });
  }

  closeVersions() {
    this.versionsFor.set(null);
    this.currentVersions.set([]);
  }

  onRestored() {
    this.load();
    if (this.versionsFor()) {
      this.api.getVersions(this.versionsFor()!).subscribe({
        next: (v) => this.currentVersions.set(v),
      });
    }
  }

  deleteFile(name: string) {
    if (!confirm(`Ta bort "${name}" och alla versioner?`)) return;
    this.api.deleteFile(name).subscribe({
      next: () => {
        this.setAction(`✓ "${name}" borttagen`, true);
        if (this.versionsFor() === name) this.closeVersions();
        this.load();
      },
      error: (e) => this.setAction(`Fel: ${e.status}`, false),
    });
  }

  deleteFolder(name: string) {
    if (!confirm(`Ta bort mappen "${name}" och allt innehåll?`)) return;
    // Strip trailing slash for the API call
    this.api.deleteFolder(name.replace(/\/$/, '')).subscribe({
      next: () => {
        this.setAction(`✓ Mappen "${name}" borttagen`, true);
        this.load();
      },
      error: (e) => this.setAction(`Fel: ${e.status}`, false),
    });
  }

  private setAction(msg: string, ok: boolean) {
    this.actionMessage.set(msg);
    this.actionSuccess.set(ok);
  }

  formatBytes(b: number): string {
    if (b < 1024) return `${b} B`;
    if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
    return `${(b / (1024 * 1024)).toFixed(1)} MB`;
  }
}

