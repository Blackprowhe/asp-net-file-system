export interface StoredFile {
  name: string;
  created: string;
  changed: string;
  file: boolean;
  bytes: number;
  extension: string;
  currentVersion: number;
}

export interface FileVersion {
  version: number;
  createdAt: string;
  bytes: number;
  isCurrent: boolean;
}

export type FileMap = Record<string, StoredFile>;

