'use client';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError } from '@/lib/api/client';
import { validateJob } from '@/lib/job-validation.mjs';
import { LatestRequest } from '@/lib/latest-request';
import { Notification, type Notice } from '@/components/notification';
import type { DatabaseDiscoveryResult, SavedDatabaseConnection } from '@/types/api';
import { BasicInformationCard, type JobDetails } from './basic-information-card';
import { DatabaseConnectionCard } from './database-connection-card';
import { DatabaseConnectionModal } from './database-connection-modal';
import { DatabaseSelector } from './database-selector';
import { BackupJobSummary } from './backup-job-summary';

export function JobForm({ onSaved, onBack, onManage }: { onSaved: () => void; onBack: () => void; onManage: () => void }) {
  const draftId = useRef<string | null>(null);
  const [job, setJob] = useState<JobDetails>({ name: '', backupDirectory: '', retentionDays: 2 });
  const [connection, setConnection] = useState<SavedDatabaseConnection | null>(null);
  const [connections, setConnections] = useState<SavedDatabaseConnection[]>([]);
  const [connecting, setConnecting] = useState(false);
  const selectionRequest = useRef<AbortController | null>(null);
  const testRequests = useRef(new LatestRequest());
  const [discovery, setDiscovery] = useState<DatabaseDiscoveryResult | null>(null);
  const [connectionId, setConnectionId] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [modal, setModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  useEffect(() => {
    let active = true;
    api.connections().then(items => { if (active) setConnections(items); }).catch(error => { if (active) setNotice({ kind: 'error', message: error.message }); });
    const tests = testRequests.current;
    return () => { active = false; selectionRequest.current?.abort(); tests.cancel(); };
  }, []);
  function disconnect() { testRequests.current.cancel(); selectionRequest.current?.abort(); setConnecting(false); setConnection(null); setDiscovery(null); setConnectionId(null); setSelected([]); setNotice(null); }
  async function connected(saved: SavedDatabaseConnection) {
    testRequests.current.cancel();
    selectionRequest.current?.abort();
    const request = new AbortController(); selectionRequest.current = request;
    setConnection(saved); setConnectionId(saved.id); setDiscovery(null); setSelected([]); setModal(false); setErrors({}); setConnecting(true);
    setConnections(items => [...items.filter(item => item.id !== saved.id), saved]);
    setNotice({ kind: 'info', message: 'Đang tải database từ kết nối đã lưu...' });
    try {
      const result = await api.discoverSavedConnection(saved.id, request.signal);
      if (!request.signal.aborted) { setDiscovery(result); setNotice({ kind: 'success', message: `Đã kết nối ${result.server.name}. Tải được ${result.databases.length} database.` }); }
    } catch (error) {
      if (!request.signal.aborted) setNotice({ kind: 'error', message: `Kết nối đã lưu. ${error instanceof Error ? error.message : 'Không tải được database.'}` });
    } finally { if (!request.signal.aborted) setConnecting(false); }
  }
  async function testSaved() {
    if (!connection) return;
    const request = testRequests.current.start();
    try { const result = await api.testSavedConnection(connection.id, request.signal); if (!request.signal.aborted) setNotice({ kind: 'success', message: `Kết nối thành công · ${result.serverName} · SQL Server ${result.version}` }); }
    catch (error) { if (!request.signal.aborted) setNotice({ kind: 'error', message: error instanceof Error ? error.message : 'Không thể kết nối.' }); }
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (saving) return;
    const validation = validateJob({ ...job, databases: selected, connectionId: connection ? 'connected' : undefined });
    setErrors(validation);
    if (Object.keys(validation).length || !connectionId || !discovery) { setNotice({ kind: 'error', message: 'Vui lòng kiểm tra các trường bắt buộc.' }); return; }
    setSaving(true); setNotice({ kind: 'info', message: 'Đang kiểm tra và lưu cấu hình...' });
    try {
      draftId.current ??= crypto.randomUUID();
      await api.saveJob({ ...job, id: draftId.current, name: job.name.trim(), backupDirectory: job.backupDirectory.trim(), connectionId, databases: selected, enabled: true });
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
          <div className="panel space-y-3 p-5">
            <label className="block text-sm font-semibold">Kết nối SQL Server<select className="job-input mt-2" value={connectionId ?? ''} onChange={e => { const item = connections.find(c => c.id === e.target.value); if (item) void connected(item); else disconnect(); }}><option value="">Chọn kết nối đã lưu</option>{connections.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
            <div className="flex flex-wrap gap-2"><button type="button" className="job-button" onClick={onManage}>Quản lý</button><button type="button" className="job-button" onClick={() => { disconnect(); setModal(true); }}>+ Thêm kết nối</button>{connection && <><button type="button" className="job-button" disabled={connecting} onClick={() => void testSaved()}>Kiểm tra</button><button type="button" className="job-button" disabled={connecting} onClick={() => void connected(connection)}>Tải lại database</button></>}</div>
          </div>
          <DatabaseConnectionCard connection={connection ? { ...connection, password: null } : null} server={discovery?.server ?? null} onConfigure={() => setModal(true)} onDisconnect={disconnect} error={errors.connectionId} />
          <DatabaseSelector key={discovery?.server.name ?? 'disconnected'} databases={discovery?.databases ?? []} selected={selected} connected={!!discovery} onChange={setSelected} error={errors.databases} />
        </div>
        <BackupJobSummary job={job} connection={connection ? { ...connection, password: null } : null} server={discovery?.server ?? null} selected={selected} saving={saving || connecting} />
      </fieldset>
    </form>
    {modal && <DatabaseConnectionModal initial={connection} onClose={() => setModal(false)} onConnected={saved => void connected(saved)} />}
  </section>;
}
