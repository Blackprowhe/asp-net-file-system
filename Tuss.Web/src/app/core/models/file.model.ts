export interface StoredFile {
  name: string;
  created: string;
  changed: string;
  file: boolean;
  bytes: number;
  extension?: string;
  content?: FileMap;
}

export interface FileVersion {
  version: number;
  createdAt: string;
  bytes: number;
  isCurrent: boolean;
}

export type FileMap = Record<string, Omit<StoredFile, 'name'>>;
