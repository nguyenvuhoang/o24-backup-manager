'use client';
import { useEffect, useRef, useState } from 'react';
import { Modal } from '@/components/modal';
import { Notification, type Notice } from '@/components/notification';
import { api, ApiError } from '@/lib/api/client';
import type { DatabaseConnectionForm, DatabaseDiscoveryResult } from '@/types/api';
import { FormField } from './form-elements';

const defaults: DatabaseConnectionForm = {
  name: '', provider: 'sqlserver', host: '', port: null, instanceName: null,
  authenticationType: 'windows', username: null, password: null, database: null,
  encrypt: true, trustServerCertificate: false, connectionTimeout: 15, commandTimeout: 30, applicationName: 'BackupManager',
};

export function DatabaseConnectionModal({ initial, onClose, onConnectStart, onConnected }: {
  initial: DatabaseConnectionForm | null;
  onClose: () => void;
  onConnectStart: () => void;
  onConnected: (form: DatabaseConnectionForm, result: DatabaseDiscoveryResult) => void;
}) {
  const [form, setForm] = useState<DatabaseConnectionForm>(() => initial ? { ...initial } : { ...defaults });
  const [busy, setBusy] = useState<'test' | 'connect' | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const controller = useRef<AbortController | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => () => controller.current?.abort(), []);
  function change<K extends keyof DatabaseConnectionForm>(key: K, value: DatabaseConnectionForm[K]) {
    setForm(current => ({ ...current, [key]: value })); setNotice(null); setErrors({});
  }
  function close() { controller.current?.abort(); onClose(); }
  async function perform(mode: 'test' | 'connect') {
    if (busy || !formRef.current?.reportValidity()) return;
    const request = new AbortController(); controller.current = request;
    setBusy(mode); setErrors({});
    setNotice({ kind: 'info', message: mode === 'test' ? 'Đang kiểm tra kết nối SQL Server...' : 'Đang kết nối và tải danh sách database...' });
    const options = { ...form, host: form.host.trim(), username: form.authenticationType === 'windows' ? null : form.username?.trim() || null, password: form.authenticationType === 'windows' ? null : form.password };
    if (mode === 'connect') onConnectStart();
    try {
      if (mode === 'test') {
        const result = await api.testConnection(options, request.signal);
        if (!request.signal.aborted) setNotice({ kind: 'success', message: `Kết nối thành công · ${result.serverName} · SQL Server ${result.version}` });
      } else {
        const result = await api.discoverDatabases(options, request.signal);
        if (!request.signal.aborted) onConnected(options, result);
      }
    } catch (error) {
      if (!request.signal.aborted) {
        setNotice({ kind: 'error', message: error instanceof Error ? error.message : 'Không thể kết nối SQL Server.' });
        if (error instanceof ApiError) setErrors(error.errors ?? {});
      }
    } finally { if (!request.signal.aborted) setBusy(null); }
  }
  return <Modal title="Cấu hình kết nối SQL Server" subtitle="Nhập thông tin kết nối đến SQL Server" onClose={close}>
    <form ref={formRef} onSubmit={e => { e.preventDefault(); void perform('connect'); }}>
      <fieldset disabled={!!busy} className="space-y-5 p-5 sm:p-6">
        <label className="block text-sm font-semibold text-slate-700">Server type<select className="job-input mt-2" value={form.provider} onChange={() => {}}><option value="sqlserver">Microsoft SQL Server</option></select></label>
        <div className="grid gap-5 sm:grid-cols-[1fr_140px]">
          <FormField label="Host / Server" name="host" required autoFocus autoComplete="off" placeholder="Hostname hoặc địa chỉ IP" value={form.host} maxLength={255} onChange={e => change('host', e.target.value)} error={errors.host?.[0]} />
          <FormField label="Port" name="port" type="number" min={1} max={65535} step={1} placeholder="1433" value={form.port ?? ''} onChange={e => change('port', e.target.value ? e.target.valueAsNumber : null)} error={errors.port?.[0]} />
        </div>
        <FormField label="Instance Name" name="instanceName" placeholder="Để trống nếu dùng default instance" value={form.instanceName ?? ''} maxLength={128} onChange={e => change('instanceName', e.target.value || null)} error={errors.instanceName?.[0]} hint={form.port ? 'Đang dùng TCP port chỉ định; Instance Name sẽ không được dùng để tìm server.' : 'Để trống Port nếu muốn kết nối bằng named instance qua SQL Browser.'} />
        <label className="block text-sm font-semibold text-slate-700">Authentication<select name="authenticationType" className="job-input mt-2" value={form.authenticationType} onChange={e => change('authenticationType', e.target.value as DatabaseConnectionForm['authenticationType'])}><option value="windows">Windows Authentication</option><option value="sqlserver">SQL Server Authentication</option></select></label>
        {form.authenticationType === 'windows' ? <p className="rounded-lg bg-slate-50 p-3 text-xs leading-5 text-slate-500">Sử dụng identity của dịch vụ backend, không phải tài khoản trình duyệt. Backend Linux/Docker cần SQL Server Authentication.</p> : <div className="grid gap-5 sm:grid-cols-2">
          <FormField label="Username" name="username" required autoComplete="off" maxLength={128} value={form.username ?? ''} onChange={e => change('username', e.target.value)} error={errors.username?.[0]} />
          <div><FormField label="Password" name="password" type={showPassword ? 'text' : 'password'} required autoComplete="new-password" maxLength={4096} value={form.password ?? ''} onChange={e => change('password', e.target.value)} error={errors.password?.[0]} /><button type="button" className="mt-2 text-xs text-blue-700" aria-pressed={showPassword} onClick={() => setShowPassword(!showPassword)}>{showPassword ? 'Ẩn password' : 'Hiện password'}</button></div>
        </div>}
        <FormField label="Database (tùy chọn)" name="database" placeholder="<Default>" maxLength={128} value={form.database ?? ''} onChange={e => change('database', e.target.value || null)} hint="Không cần nhập database để discover. Khi để trống, backend dùng master." error={errors.database?.[0]} />
        <details className="rounded-xl border border-slate-200 p-4"><summary className="cursor-pointer text-sm font-semibold">Tùy chọn nâng cao</summary><div className="mt-5 grid gap-5 sm:grid-cols-2">
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.encrypt} onChange={e => change('encrypt', e.target.checked)} />Encrypt</label>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.trustServerCertificate} onChange={e => change('trustServerCertificate', e.target.checked)} />Trust Server Certificate</label>
          <FormField label="Connection Timeout (giây)" type="number" required min={1} max={120} value={form.connectionTimeout || ''} onChange={e => change('connectionTimeout', e.target.valueAsNumber)} error={errors.connectionTimeout?.[0]} />
          <FormField label="Command Timeout (giây)" type="number" required min={1} max={600} value={form.commandTimeout || ''} onChange={e => change('commandTimeout', e.target.valueAsNumber)} error={errors.commandTimeout?.[0]} />
          <div className="sm:col-span-2"><FormField label="Application Name" required maxLength={128} value={form.applicationName} onChange={e => change('applicationName', e.target.value)} error={errors.applicationName?.[0]} /></div>
        </div></details>
      </fieldset>
      <div className="px-5 pb-5 sm:px-6"><Notification notice={notice} /></div>
      <footer className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 bg-slate-50 px-5 py-4 sm:px-6">
        <button type="button" className="job-button" disabled={!!busy} onClick={() => void perform('test')}>{busy === 'test' ? 'Đang kiểm tra...' : 'Kiểm tra kết nối'}</button>
        <div className="flex gap-3"><button type="button" className="job-button" onClick={close}>Hủy</button><button type="submit" className="job-primary" disabled={!!busy}>{busy === 'connect' ? 'Đang tải database...' : 'Kết nối'}</button></div>
      </footer>
    </form>
  </Modal>;
}

