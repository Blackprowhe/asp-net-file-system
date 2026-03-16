import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { NavigationService } from '../../core/services/navigation.service';
import { StoredFile, FileVersion } from '../../core/models/file.model';
import { FileVersionsComponent } from '../file-versions/file-versions.component';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import {
  FileIconComponent, FolderIconComponent, ChevronRightIconComponent,
  ClockIconComponent, DownloadIconComponent, TrashIconComponent,
  ResetIconComponent,
} from '../../shared/icons/icons';

@Component({
  selector: 'app-file-browser',
  standalone: true,
  imports: [
    CommonModule, FileVersionsComponent, FileSizePipe,
    FileIconComponent, FolderIconComponent, ChevronRightIconComponent,
    ClockIconComponent, DownloadIconComponent, TrashIconComponent,
    ResetIconComponent,
  ],
  templateUrl: './file-browser.component.html',
})
export class FileBrowserComponent implements OnInit {
  private api = inject(ApiService);
  nav = inject(NavigationService);

  loading = signal(false);
  versionsFor = signal<string | null>(null);
  currentVersions = signal<FileVersion[]>([]);
  actionMessage = signal('');
  actionSuccess = signal(false);

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.api.getFiles().subscribe({
      next: (map) => {
        const flat: StoredFile[] = [];
        this.flattenEntries(map, '', flat);
        this.nav.allEntries.set(flat);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** Plattar ut det nästlade trädet till en flat lista med fulla sökvägar */
  private flattenEntries(map: Record<string, any>, prefix: string, out: StoredFile[]) {
    for (const [key, meta] of Object.entries(map)) {
      const fullName = prefix ? `${prefix}/${key}` : key;
      out.push({
        name: fullName, created: meta.created, changed: meta.changed,
        file: meta.file, bytes: meta.bytes, extension: meta.extension,
      });
      if (!meta.file && meta.content) {
        this.flattenEntries(meta.content, fullName, out);
      }
    }
  }

  navigateToFolder(entry: StoredFile) { this.nav.navigateTo(entry.name); }

  downloadUrl(name: string): string {
    return `/api/files/${name.split('/').map(encodeURIComponent).join('/')}`;
  }

  showVersions(entry: StoredFile) {
    this.versionsFor.set(entry.name);
    this.currentVersions.set([]);
    this.api.getVersions(entry.name).subscribe({ next: (v) => this.currentVersions.set(v) });
  }

  closeVersions() { this.versionsFor.set(null); this.currentVersions.set([]); }

  onRestored() {
    this.load();
    if (this.versionsFor()) {
      this.api.getVersions(this.versionsFor()!).subscribe({ next: (v) => this.currentVersions.set(v) });
    }
  }

  deleteFile(name: string) {
    if (!confirm(`Ta bort "${this.nav.shortName(name)}" och alla versioner?`)) return;
    this.api.deleteFile(name).subscribe({
      next: () => {
      next: () => { this.setAction(`"${this.nav.shortName(name)}" borttagen`, true); if (this.versionsFor() === name) this.closeVersions(); this.load(); },
      error: (e) => this.setAction(`Fel: ${e.status}`, false),
    });
  }

  deleteFolder(name: string) {
    if (!confirm(`Ta bort mappen "${this.nav.shortName(name)}" och allt innehåll?`)) return;
    this.api.deleteFolder(name).subscribe({
      next: () => {
        this.setAction(`✓ Mappen "${this.nav.shortName(name)}" borttagen`, true);
        // Om vi stod i den borttagna mappen, gå till parent
        if (this.nav.currentPath() === name || this.nav.currentPath().startsWith(name + '/')) {
          const parentIdx = name.lastIndexOf('/');
          this.nav.navigateTo(parentIdx === -1 ? '' : name.slice(0, parentIdx));
        }
        this.load();
      },
      error: (e) => this.setAction(`Fel: ${e.status}`, false),
    });
  }

  private setAction(msg: string, ok: boolean) {
    this.actionMessage.set(msg);
    this.actionSuccess.set(ok);
  }
}
