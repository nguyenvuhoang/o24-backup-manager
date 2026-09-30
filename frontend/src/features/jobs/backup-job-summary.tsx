import type { DatabaseConnectionForm, DatabaseServerInfo } from '@/types/api';
import type { JobDetails } from './basic-information-card';

export function ProcessingPipeline() {
  return <div><div className="flex flex-wrap items-center gap-2 text-xs font-medium">{['Backup', 'Verify', 'ZIP', 'SCP', 'Telegram'].map((stage, i) => <span key={stage} className="contents">{i > 0 && <span className="text-slate-300">→</span>}<span className={`rounded-md px-2 py-1.5 ${i < 3 ? 'bg-blue-50 text-blue-700' : 'bg-slate-100 text-slate-500'}`}>{stage}</span></span>)}</div><p className="mt-3 text-xs leading-5 text-slate-500">SCP và Telegram chỉ chạy khi được bật trong cấu hình job. Job mới hiện chưa bật hai bước này.</p></div>;
}
export function BackupJobSummary({ job, connection, server, selected, saving }: { job: JobDetails; connection: DatabaseConnectionForm | null; server: DatabaseServerInfo | null; selected: string[]; saving: boolean }) {
  return <aside className="panel h-fit lg:sticky lg:top-6">
    <header className="border-b border-slate-100 p-6"><h2 className="font-semibold">Tóm tắt cấu hình</h2><p className="mt-1 text-sm text-slate-500">Kiểm tra lại thông tin trước khi lưu</p></header>
    <div className="space-y-5 p-6 text-sm">
      <div><h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-slate-400">Thông tin cơ bản</h3><dl className="space-y-3"><div><dt className="text-slate-500">Tên job</dt><dd className="mt-1 break-words font-medium">{job.name || 'Chưa nhập'}</dd></div><div><dt className="text-slate-500">Thư mục backup</dt><dd className="mt-1 break-all font-medium text-xs">{job.backupDirectory || 'Chưa nhập'}</dd></div><div className="flex justify-between"><dt className="text-slate-500">Giữ file</dt><dd>{Number.isFinite(job.retentionDays) ? job.retentionDays : '—'} ngày</dd></div></dl></div>
      <div className="border-t border-slate-100 pt-5"><h3 className="mb-2 font-semibold">SQL Server</h3><p className="break-all text-slate-600">{server?.name || 'Chưa kết nối'}</p>{connection && <p className="mt-1 text-xs text-slate-500">{connection.authenticationType === 'windows' ? 'Windows Authentication' : 'SQL Server Authentication'}</p>}</div>
      <div className="border-t border-slate-100 pt-5"><h3 className="mb-3 font-semibold">Database ({selected.length})</h3>{selected.length ? <div className="flex flex-wrap gap-2">{selected.slice(0, 3).map(name => <span key={name} className="max-w-full break-all rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-600">{name}</span>)}{selected.length > 3 && <span className="py-1 text-xs text-blue-700">+ {selected.length - 3} database khác</span>}</div> : <p className="text-slate-400">Chưa chọn database</p>}</div>
      <div className="border-t border-slate-100 pt-5"><h3 className="mb-3 font-semibold">Quy trình xử lý</h3><ProcessingPipeline /></div>
      <button type="submit" disabled={saving} className="job-primary w-full">{saving ? 'Đang lưu job...' : 'Lưu job'}</button>
    </div>
  </aside>;
}

