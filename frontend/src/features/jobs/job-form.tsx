'use client';
import { useRef, useState, type FormEvent } from 'react';
import { api, ApiError } from '@/lib/api/client';
import { validateJob } from '@/lib/job-validation.mjs';
import { Notification, type Notice } from '@/components/notification';
import type { DatabaseConnectionForm, DatabaseDiscoveryResult } from '@/types/api';
import { BasicInformationCard, type JobDetails } from './basic-information-card';
import { DatabaseConnectionCard } from './database-connection-card';
import { DatabaseConnectionModal } from './database-connection-modal';
import { DatabaseSelector } from './database-selector';
import { BackupJobSummary } from './backup-job-summary';

export function JobForm({ onSaved, onBack }: { onSaved: () => void; onBack: () => void }) {
  const draftId = useRef<string | null>(null);
  const [job, setJob] = useState<JobDetails>({ name: '', backupDirectory: '', retentionDays: 2 });
  const [connection, setConnection] = useState<DatabaseConnectionForm | null>(null);
  const [discovery, setDiscovery] = useState<DatabaseDiscoveryResult | null>(null);
  const [connectionId, setConnectionId] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [modal, setModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  function disconnect() { setConnection(null); setDiscovery(null); setConnectionId(null); setSelected([]); setNotice(null); }
  function connected(form: DatabaseConnectionForm, result: DatabaseDiscoveryResult) {
    setConnection(form); setDiscovery(result); setSelected([]); setConnectionId(null); setModal(false); setErrors({});
    setNotice({ kind: 'success', message: `Đã kết nối ${result.server.name}. Tải được ${result.databases.length} database.` });
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (saving) return;
    const validation = validateJob({ ...job, databases: selected, connectionId: connection ? 'connected' : undefined });
    setErrors(validation);
    if (Object.keys(validation).length || !connection) { setNotice({ kind: 'error', message: 'Vui lòng kiểm tra các trường bắt buộc.' }); return; }
    setSaving(true); setNotice({ kind: 'info', message: 'Đang kiểm tra và lưu cấu hình...' });
    try {
      let id = connectionId;
      if (!id) {
        const saved = await api.saveConnection(connection);
        id = saved.id; setConnectionId(id);
        setConnection(current => current ? { ...current, password: null } : null);
      }
      draftId.current ??= crypto.randomUUID();
      await api.saveJob({ ...job, id: draftId.current, name: job.name.trim(), backupDirectory: job.backupDirectory.trim(), connectionId: id, databases: selected, enabled: true });
      onSaved();
    } catch (error) {
      if (error instanceof ApiError && ['CONNECTION_FAILED', 'DISCOVERY_FAILED', 'DATABASE_UNAVAILABLE', 'NOT_FOUND', 'CONNECTION_CHANGED'].includes(error.code ?? '')) disconnect();
      if (error instanceof ApiError && error.errors) setErrors(Object.fromEntries(Object.entries(error.errors).map(([key, values]) => [key, values[0]])));
      setNotice({ kind: 'error', message: error instanceof Error ? error.message : 'Không thể lưu job.' });
    } finally { setSaving(false); }
  }
  return <section>
    <header className="mb-7"><button type="button" onClick={onBack} disabled={saving} className="mb-4 text-sm text-slate-500 hover:text-blue-700">← Quay lại</button><h1 className="text-2xl font-bold tracking-tight sm:text-3xl">Tạo Backup Job</h1><p className="mt-2 text-sm text-slate-500">SQL Server → Verify → ZIP → SCP → Telegram</p></header>
    <div className="mb-5"><Notification notice={notice} onDismiss={() => setNotice(null)} /></div>
    <form onSubmit={save} noValidate>
      <fieldset disabled={saving} className="grid min-w-0 gap-6 lg:grid-cols-[minmax(0,7fr)_minmax(290px,3fr)]">
        <div className="min-w-0 space-y-6">
          <BasicInformationCard value={job} onChange={setJob} errors={errors} />
          <DatabaseConnectionCard connection={connection} server={discovery?.server ?? null} onConfigure={() => setModal(true)} onDisconnect={disconnect} error={errors.connectionId} />
          <DatabaseSelector key={discovery?.server.name ?? 'disconnected'} databases={discovery?.databases ?? []} selected={selected} connected={!!discovery} onChange={setSelected} error={errors.databases} />
        </div>
        <BackupJobSummary job={job} connection={connection} server={discovery?.server ?? null} selected={selected} saving={saving} />
      </fieldset>
    </form>
    {modal && <DatabaseConnectionModal initial={connection} onClose={() => setModal(false)} onConnectStart={disconnect} onConnected={connected} />}
  </section>;
}
