import type { BackupJob, BackupRun, CreateBackupJobRequest, DatabaseConnectionForm, DatabaseDiscoveryResult, DatabaseTestResult, DiscoveryResult, SavedDatabaseConnection, SqlServerOptions } from '@/types/api';

export class ApiError extends Error {
  constructor(message: string, public code?: string, public errors?: Record<string, string[]>) { super(message); }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api/v1${path}`, { ...init, headers: { 'content-type': 'application/json', ...init?.headers } });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new ApiError(body?.message ?? (response.status === 409 ? 'Job đang chạy.' : `Backend trả về lỗi ${response.status}.`), body?.errorCode, body?.errors);
  }
  return response.status === 202 || response.status === 204 ? undefined as T : response.json();
}

export const api = {
  jobs: () => request<BackupJob[]>('/jobs'),
  saveJob: (job: BackupJob | CreateBackupJobRequest) => request<BackupJob>('/jobs', { method: 'POST', body: JSON.stringify(job) }),
  testConnection: (form: DatabaseConnectionForm, signal?: AbortSignal) => request<DatabaseTestResult>('/database-connections/test', { method: 'POST', body: JSON.stringify(form), signal }),
  discoverDatabases: (form: DatabaseConnectionForm, signal?: AbortSignal) => request<DatabaseDiscoveryResult>('/database-connections/discover', { method: 'POST', body: JSON.stringify(form), signal }),
  saveConnection: (form: DatabaseConnectionForm) => request<SavedDatabaseConnection>('/database-connections', { method: 'POST', body: JSON.stringify(form) }),
  connections: () => request<SavedDatabaseConnection[]>('/database-connections'),
  discover: (options: SqlServerOptions) => request<DiscoveryResult>('/connections/sql-server/databases', { method: 'POST', body: JSON.stringify(options) }),
  runs: () => request<BackupRun[]>('/runs'),
  run: (id: string) => request<void>(`/jobs/${id}/run`, { method: 'POST' }),
  cancel: (id: string) => request<void>(`/jobs/${id}/cancel`, { method: 'POST' }),
  storeSecret: (name: string, value: string) => request<void>('/secrets', { method: 'POST', body: JSON.stringify({ name, value }) })
};
