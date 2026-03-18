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
  draggedEntry = signal<StoredFile | null>(null);
  dropTarget = signal<string | null>(null);


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
onDragStart(entry: StoredFile, e: DragEvent) {
  this.draggedEntry.set(entry);
  e.dataTransfer?.setData('application/x-tuss-entry', entry.name);
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
}

onDragOver(e: DragEvent) {
  e.preventDefault();
  e.stopPropagation();
  this.isDragOver.set(true);
}

onDragLeave(e: DragEvent) {
  e.preventDefault();
  e.stopPropagation();
  this.isDragOver.set(false);
}

onDrop(e: DragEvent) {
  e.preventDefault();
  e.stopPropagation();
  this.isDragOver.set(false);

  const sourcePath = e.dataTransfer?.getData('application/x-tuss-entry');
  const targetPath = this.nav.currentPath();

  if (sourcePath) {
    // Intern flytt till nuvarande mapp (om den inte redan är där)
    const parent = sourcePath.includes('/') ? sourcePath.substring(0, sourcePath.lastIndexOf('/')) : '';
    if (parent === targetPath) return;

    const shortName = sourcePath.split('/').pop()!;
    const newPath = targetPath ? `${targetPath}/${shortName}` : shortName;

    this.api.moveEntry(sourcePath, newPath).subscribe({
      next: () => this.load(),
      error: (err) => this.setAction(`Fel: ${err.status}`, false)
    });
    return;
  }

  // Extern uppladdning
  const files = e.dataTransfer?.files;
  if (!files?.length) return;
  this.loading.set(true);
  this.api.bulkUpload(targetPath, Array.from(files)).subscribe({
    next: () => {
      this.setAction(`${files.length} filer uppladdade`, true);
      this.load();
    },
    error: (err) => {
      this.loading.set(false);
      this.setAction(`Fel vid uppladdning: ${err.status}`, false);
    }
  });
}

onDragOverRow(e: DragEvent, entry: StoredFile) {
  if (entry.file) return;
  e.preventDefault();
  e.stopPropagation();
  this.dropTarget.set(entry.name);
}

onDragLeaveRow(e: DragEvent) {
  this.dropTarget.set(null);
}

onDropOnFolder(e: DragEvent, targetEntry: StoredFile) {
  if (targetEntry.file) return;
  e.preventDefault();
  e.stopPropagation();
  this.dropTarget.set(null);

  const sourcePath = e.dataTransfer?.getData('application/x-tuss-entry');

  if (sourcePath) {
    if (sourcePath === targetEntry.name) return;
    if (sourcePath.startsWith(targetEntry.name + '/')) return; // Kan inte flytta mapp in i sig själv

    const shortName = sourcePath.split('/').pop()!;
    const newPath = `${targetEntry.name}/${shortName}`;

    this.api.moveEntry(sourcePath, newPath).subscribe({
      next: () => this.load(),
      error: (err) => this.setAction(`Fel: ${err.status}`, false)
    });
  } else {
    const files = e.dataTransfer?.files;
    if (!files?.length) return;
    this.loading.set(true);
    this.api.bulkUpload(targetEntry.name, Array.from(files)).subscribe({
      next: () => {
        this.setAction(`${files.length} filer uppladdade till ${targetEntry.name}`, true);
        this.load();
      },
      error: (err) => {
        this.loading.set(false);
        this.setAction(`Fel: ${err.status}`, false);
      }
    });
  }
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

    this.loading.set(true);
    this.api.bulkUpload(targetPath, fileList).subscribe({
      next: () => {
        this.setAction(`${fileList.length} filer uppladdade`, true);
        this.load();
      },
      error: (err) => {
        if (err.status === 409) {
            this.api.replaceFile(fullPath, file).subscribe({
                this.load();
              },
                this.loading.set(false);
              }
          } else {
          }
          this.loading.set(false);
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
