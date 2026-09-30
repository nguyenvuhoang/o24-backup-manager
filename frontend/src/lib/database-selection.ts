import type { DatabaseInfo } from '../types/api';

export const selectableNames = (databases: DatabaseInfo[]) => databases.filter(d => d.status === 'ONLINE' && d.isAccessible).map(d => d.name);
export const filterDatabases = (databases: DatabaseInfo[], search: string) => databases.filter(d => d.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
export function formatDatabaseSize(sizeMb: number | null): string {
  if (sizeMb === null) return '—';
  if (sizeMb < 1) return `${Number((sizeMb * 1024).toFixed(1))} KB`;
  if (sizeMb < 1024) return `${Number(sizeMb.toFixed(1))} MB`;
  if (sizeMb < 1048576) return `${Number((sizeMb / 1024).toFixed(1))} GB`;
  return `${Number((sizeMb / 1048576).toFixed(1))} TB`;
}
