import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { NavigationService } from '../../core/services/navigation.service';
import { StoredFile, FileVersion } from '../../core/models/file.model';
import { FileVersionsComponent } from '../file-versions/file-versions.component';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import {
  FileIconComponent, FolderIconComponent, ChevronRightIconComponent,
  ClockIconComponent, DownloadIconComponent, TrashIconComponent,
  ResetIconComponent, ImageIconComponent, VideoIconComponent,
  CodeIconComponent, TextIconComponent, ArchiveIconComponent,
  CrossIconComponent, PlusIconComponent, UploadIconComponent,
  ScissorsIconComponent,
} from '../../shared/icons/icons';

const IMAGE_EXTS  = new Set(['.jpg','.jpeg','.png','.gif','.webp','.svg','.avif','.bmp']);
const VIDEO_EXTS  = new Set(['.mp4','.webm','.mov','.avi','.mkv']);
const CODE_EXTS   = new Set(['.ts','.js','.cs','.html','.css','.scss','.json','.xml','.yaml','.yml','.py','.go','.rs','.java','.cpp','.c','.h','.sh','.md']);
const TEXT_EXTS   = new Set(['.txt','.log','.csv','.env','.ini','.cfg']);
const ARCHIVE_EXTS= new Set(['.zip','.tar','.gz','.rar','.7z','.bz2']);

type IconType = 'image'|'video'|'code'|'text'|'archive'|'file';

@Component({
  selector: 'app-file-browser',
  standalone: true,
  imports: [
    CommonModule, FormsModule, FileVersionsComponent, FileSizePipe,
    FileIconComponent, FolderIconComponent, ChevronRightIconComponent,
    ClockIconComponent, DownloadIconComponent, TrashIconComponent,
    ResetIconComponent, ImageIconComponent, VideoIconComponent,
    CodeIconComponent, TextIconComponent, ArchiveIconComponent,
    CrossIconComponent, PlusIconComponent, UploadIconComponent, ScissorsIconComponent,
  ],
  templateUrl: './file-browser.component.html',
  styleUrl: './file-browser.component.scss',
})
export class FileBrowserComponent implements OnInit {
  private api = inject(ApiService);
  nav = inject(NavigationService);

  loading = signal(false);
  versionsFor = signal<string | null>(null);
  currentVersions = signal<FileVersion[]>([]);
  actionMessage = signal('');
  actionSuccess = signal(false);

  // New Folder / Upload
  isCreatingFolder = signal(false);
  newFolderName = signal('');
  showNewMenu = signal(false);

  // Drag & drop
  isDragOver = signal(false);

  // Bildförhandsvisning
  previewUrl = signal<string | null>(null);
  previewName = signal('');

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

  // ── Icon helper ──────────────────────────────────────────────────────────
  iconType(entry: StoredFile): IconType {
    if (!entry.file) return 'file';
    const ext = (entry.extension ?? '').toLowerCase();
    if (IMAGE_EXTS.has(ext))   return 'image';
    if (VIDEO_EXTS.has(ext))   return 'video';
    if (CODE_EXTS.has(ext))    return 'code';
    if (TEXT_EXTS.has(ext))    return 'text';
    if (ARCHIVE_EXTS.has(ext)) return 'archive';
    return 'file';
  }

  isImage(entry: StoredFile): boolean {
    return IMAGE_EXTS.has((entry.extension ?? '').toLowerCase());
  }

  // ── Navigation ───────────────────────────────────────────────────────────
  navigateToFolder(entry: StoredFile) { this.nav.navigateTo(entry.name); }

  downloadUrl(name: string): string {
    return `/api/files/${name.split('/').map(encodeURIComponent).join('/')}`;
  }

  // ── Preview ──────────────────────────────────────────────────────────────
  openPreview(entry: StoredFile) {
    this.previewUrl.set(this.downloadUrl(entry.name));
    this.previewName.set(this.nav.shortName(entry.name));
  }

  onFileNameClick(entry: StoredFile, event: MouseEvent) {
    if (this.isImage(entry)) {
      event.stopPropagation();
      this.openPreview(entry);
    }
  }

  closePreview() {
    this.previewUrl.set(null);
    this.previewName.set('');
  }

  // ── Drag & drop ──────────────────────────────────────────────────────────
  onDragOver(e: DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    this.isDragOver.set(true);
  }

  onDragLeave(e: DragEvent) {
    e.preventDefault();
    this.isDragOver.set(false);
  }

  onDrop(e: DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    this.isDragOver.set(false);
    const files = e.dataTransfer?.files;
    if (!files?.length) return;
    const prefix = this.nav.currentPath() ? this.nav.currentPath() + '/' : '';
    Array.from(files).forEach(file => {
      const fullPath = prefix + file.name;
      this.api.uploadFile(fullPath, file).subscribe({
        next: () => this.load(),
        error: (err) => {
          if (err.status === 409) {
            // Finns redan — gör PUT (ny version)
            this.api.replaceFile(fullPath, file).subscribe({ next: () => this.load() });
          }
        },
      });
    });
  }

  // ── Versions ─────────────────────────────────────────────────────────────
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

  // ── New Actions ──────────────────────────────────────────────────────────
  toggleNewMenu() {
    this.showNewMenu.update(v => !v);
  }

  startCreateFolder() {
    this.isCreatingFolder.set(true);
    this.newFolderName.set('');
    this.showNewMenu.set(false);
  }

  cancelCreateFolder() {
    this.isCreatingFolder.set(false);
    this.newFolderName.set('');
  }

  confirmCreateFolder() {
    const name = this.newFolderName().trim();
    if (!name) {
      this.cancelCreateFolder();
      return;
    }

    const prefix = this.nav.currentPath() ? this.nav.currentPath() + '/' : '';
    const fullPath = prefix + name;

    this.loading.set(true);
    this.api.createFolder(fullPath).subscribe({
      next: () => {
        this.isCreatingFolder.set(false);
        this.newFolderName.set('');
        this.load();
        this.setAction(`Mappen "${name}" skapades`, true);
      },
      error: (e) => {
        this.loading.set(false);
        this.setAction(e.status === 409 ? `Mappen finns redan.` : `Fel: ${e.status}`, false);
      }
    });
  }

  triggerUpload(input: HTMLInputElement) {
    input.click();
    this.showNewMenu.set(false);
  }

  onFileSelected(files: FileList | null) {
    if (!files?.length) return;
    const file = files[0];
    const prefix = this.nav.currentPath() ? this.nav.currentPath() + '/' : '';
    const fullPath = prefix + file.name;

    this.loading.set(true);
    this.api.uploadFile(fullPath, file).subscribe({
      next: () => {
        this.load();
        this.setAction(`"${file.name}" uppladdad`, true);
      },
      error: (err) => {
        if (err.status === 409) {
          if (confirm(`"${file.name}" finns redan. Vill du skapa en ny version?`)) {
            this.api.replaceFile(fullPath, file).subscribe({
              next: () => {
                this.load();
                this.setAction(`Ny version av "${file.name}" skapad`, true);
              },
              error: (e) => {
                this.loading.set(false);
                this.setAction(`Fel: ${e.status}`, false);
              }
            });
          } else {
            this.loading.set(false);
          }
        } else {
          this.loading.set(false);
          this.setAction(`Fel: ${err.status}`, false);
        }
      },
    });
  }

  // ── Delete ────────────────────────────────────────────────────────────────
  deleteFile(name: string) {
    if (!confirm(`Ta bort "${this.nav.shortName(name)}"?`)) return;
    this.api.deleteFile(name).subscribe({
      next: () => { this.setAction(`"${this.nav.shortName(name)}" borttagen`, true); if (this.versionsFor() === name) this.closeVersions(); this.load(); },
      error: (e) => this.setAction(`Fel: ${e.status}`, false),
    });
  }

  deleteFolder(name: string) {
    if (!confirm(`Ta bort mappen "${this.nav.shortName(name)}" och allt innehåll?`)) return;
    this.api.deleteFolder(name).subscribe({
      next: () => {
        this.setAction(`Mappen "${this.nav.shortName(name)}" borttagen`, true);
        if (this.nav.currentPath() === name || this.nav.currentPath().startsWith(name + '/')) {
          const idx = name.lastIndexOf('/');
          this.nav.navigateTo(idx === -1 ? '' : name.slice(0, idx));
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
