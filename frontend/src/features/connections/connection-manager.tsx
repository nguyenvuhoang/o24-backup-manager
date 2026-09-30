'use client';
import { useEffect, useState } from 'react';
import { api } from '@/lib/api/client';
import { DATABASE_PROVIDERS, normalizeConnectionPort } from '@/lib/database-providers';
import { Notification, type Notice } from '@/components/notification';
import { DatabaseConnectionModal } from '@/features/jobs/database-connection-modal';
import type { SavedDatabaseConnection } from '@/types/api';

export function ConnectionManager() {
  const [items, setItems] = useState<SavedDatabaseConnection[]>([]);
  const [editing, setEditing] = useState<SavedDatabaseConnection | null | undefined>(undefined);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [deleting, setDeleting] = useState<string | null>(null);
  const [status, setStatus] = useState<Record<string, string>>({});
  useEffect(() => {
    let active = true;
    api.connections().then(data => { if (active) setItems(data); })
      .catch(error => { if (active) setNotice({ kind: 'error', message: error.message }); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);
  async function test(item: SavedDatabaseConnection) {
    setBusy(item.id);
    try {
      const result = await api.testSavedConnection(item.id);
      const message = `Kết nối thành công · ${result.serverName} · SQL Server ${result.version}`;
      setStatus(current => ({ ...current, [item.id]: message })); setNotice({ kind: 'success', message });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Không thể kết nối.';
      setStatus(current => ({ ...current, [item.id]: message })); setNotice({ kind: 'error', message });
    } finally { setBusy(null); }
  }
  async function remove(id: string) {
    setBusy(id);
    try { await api.deleteConnection(id); setItems(current => current.filter(item => item.id !== id)); setDeleting(null); setNotice({ kind: 'success', message: 'Đã xóa kết nối.' }); }
    catch (error) { setNotice({ kind: 'error', message: error instanceof Error ? error.message : 'Không thể xóa kết nối.' }); }
    finally { setBusy(null); }
  }
  function saved(item: SavedDatabaseConnection) {
    setItems(current => [...current.filter(c => c.id !== item.id), item]); setEditing(undefined);
    setStatus(current => ({ ...current, [item.id]: 'Đã kiểm tra và lưu thành công' }));
    setNotice({ kind: 'success', message: `Đã lưu kết nối ${item.name}.` });
  }
  return <section>
    <header className="mb-6 flex flex-wrap items-start justify-between gap-4"><div><p className="mb-2 text-sm text-slate-500">Cấu hình</p><h1 className="text-2xl font-bold">Kết nối Database</h1><p className="mt-2 text-sm text-slate-500">Lưu kết nối để tái sử dụng cho các backup job.</p></div><button className="job-primary" onClick={() => setEditing(null)}>+ Thêm kết nối</button></header>
    <div className="mb-5"><Notification notice={notice} onDismiss={() => setNotice(null)} /></div>
    <div className="panel overflow-x-auto"><table className="w-full text-left text-sm"><thead className="border-b border-slate-200 bg-slate-50"><tr>{['Tên', 'Provider', 'Server', 'Authentication', 'Lần kiểm tra trong phiên', 'Thao tác'].map(name => <th key={name} className="p-4 font-semibold">{name}</th>)}</tr></thead><tbody>
      {items.map(item => { const connection = normalizeConnectionPort(item); return <tr key={item.id} className="border-b border-slate-100"><td className="p-4 font-semibold">{item.name}</td><td className="p-4">{DATABASE_PROVIDERS[item.provider].displayName}</td><td className="p-4">{connection.port ? `tcp:${item.host},${connection.port}` : `${item.host}\\${item.instanceName ?? ''}`}</td><td className="p-4">{item.authenticationType === 'windows' ? 'Windows Authentication' : 'SQL Server Authentication'}</td><td className="max-w-xs p-4">{status[item.id] ?? 'Chưa kiểm tra trong phiên này'}</td><td className="p-4"><div className="flex flex-wrap gap-2"><button disabled={!!busy} className="job-button" onClick={() => void test(item)}>{busy === item.id ? 'Đang xử lý...' : 'Test'}</button><button disabled={!!busy} className="job-button" onClick={() => setEditing(item)}>Edit</button><button disabled={!!busy} className="job-button text-red-600" onClick={() => setDeleting(item.id)}>Delete</button></div>{deleting === item.id && <div className="mt-3 space-y-2"><p>Xóa kết nối “{item.name}”?</p><button disabled={!!busy} className="job-button text-red-600" onClick={() => void remove(item.id)}>Xóa kết nối</button> <button disabled={!!busy} className="job-button" onClick={() => setDeleting(null)}>Hủy</button></div>}</td></tr>; })}
      {!items.length && <tr><td colSpan={6} className="p-8 text-center text-slate-500">{loading ? 'Đang tải kết nối...' : 'Chưa có kết nối đã lưu.'}</td></tr>}
    </tbody></table></div>
    {editing !== undefined && <DatabaseConnectionModal initial={editing} onClose={() => setEditing(undefined)} onConnected={saved} />}
  </section>;
}
