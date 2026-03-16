import { Component } from '@angular/core';
import { FileBrowserComponent } from './features/file-browser/file-browser.component';
import { FileUploadComponent } from './features/file-upload/file-upload.component';
import { FolderCreateComponent } from './features/folder-create/folder-create.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FileBrowserComponent, FileUploadComponent, FolderCreateComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  title = 'Tuss';
}
