import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FileMap, FileVersion } from '../models/file.model';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private base = '/api';

  constructor(private http: HttpClient) {}

  // ── Filer ────────────────────────────────────────────────────────────────

  getFiles(): Observable<FileMap> {
    return this.http.get<FileMap>(`${this.base}/files`);
  }

  uploadFile(name: string, body: Blob): Observable<void> {
    return this.http.post<void>(
      `${this.base}/files/${encodeURIComponent(name)}`,
      body,
      { headers: new HttpHeaders({ 'Content-Type': 'application/octet-stream' }) }
    );
  }

  replaceFile(name: string, body: Blob): Observable<void> {
    return this.http.put<void>(
      `${this.base}/files/${encodeURIComponent(name)}`,
      body,
      { headers: new HttpHeaders({ 'Content-Type': 'application/octet-stream' }) }
    );
  }

  deleteFile(name: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/files/${encodeURIComponent(name)}`);
  }

  downloadFile(name: string): Observable<Blob> {
    return this.http.get(`${this.base}/files/${encodeURIComponent(name)}`, {
      responseType: 'blob',
    });
  }

  // ── Versioner ─────────────────────────────────────────────────────────────

  getVersions(name: string): Observable<FileVersion[]> {
    return this.http.get<FileVersion[]>(
      `${this.base}/files/${encodeURIComponent(name)}/versions`
    );
  }

  restoreVersion(name: string, version: number): Observable<void> {
    return this.http.post<void>(
      `${this.base}/files/${encodeURIComponent(name)}/versions/${version}/restore`,
      null
    );
  }

  // ── Mappar ────────────────────────────────────────────────────────────────

  createFolder(path: string): Observable<void> {
    return this.http.post<void>(`${this.base}/folders/${encodeURIComponent(path)}`, null);
  }

  deleteFolder(path: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/folders/${encodeURIComponent(path)}`);
  }
}

