export function validateJob(job: { name?: string; databases?: string[]; backupDirectory?: string; retentionDays?: number; connectionId?: string }): Record<string, string>;
