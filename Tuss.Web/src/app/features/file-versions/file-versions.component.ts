import { Component, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FileVersion } from '../../core/models/file.model';
import { ApiService } from '../../core/services/api.service';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import {
  DotFilledIconComponent, CircleIconComponent, CrossIconComponent,
  DownloadIconComponent, ResetIconComponent,
} from '../../shared/icons/icons';

@Component({
  selector: 'app-file-versions',
  standalone: true,
  imports: [
    CommonModule, FileSizePipe,
    DotFilledIconComponent, CircleIconComponent, CrossIconComponent,
    DownloadIconComponent, ResetIconComponent,
  ],
  templateUrl: './file-versions.component.html',
})
export class FileVersionsComponent {
  private api = inject(ApiService);

  fileName = input.required<string>();
  versions = input.required<FileVersion[]>();
  closed = output<void>();
  restored = output<void>();

  restoring = signal(false);
  message = signal('');
  success = signal(false);

  downloadUrl(version: number): string {
    const encoded = this.fileName().split('/').map(encodeURIComponent).join('/');
    return `/api/files/${encoded}/versions/${version}`;
  }

  restore(version: number) {
    this.restoring.set(true);
    this.message.set('');
    this.api.restoreVersion(this.fileName(), version).subscribe({
      next: () => {
        this.success.set(true);
        this.message.set(`Återställd till v${version}`);
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
}
