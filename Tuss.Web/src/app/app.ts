import { Component } from '@angular/core';
import { FileBrowserComponent } from './features/file-browser/file-browser.component';
import { SidebarComponent } from './features/sidebar/sidebar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FileBrowserComponent, SidebarComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  title = 'Tuss';
}
