import type { DatabaseConnectionForm, DatabaseServerInfo } from '@/types/api';
import { ConfigCard } from './form-elements';

export function DatabaseConnectionCard({ connection, server, onConfigure, onDisconnect, error }: { connection: DatabaseConnectionForm | null; server: DatabaseServerInfo | null; onConfigure: () => void; onDisconnect: () => void; error?: string }) {
  return <ConfigCard number={2} title="Kết nối SQL Server" subtitle="Thiết lập kết nối để lấy danh sách database">
    {connection ? <div className="rounded-xl border border-slate-200 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-start gap-3"><span aria-hidden="true" className="rounded-lg bg-blue-50 p-3 text-blue-700">▤</span><div className="min-w-0"><p className="break-all font-semibold">{connection.name}</p><p className="mt-1 break-all text-sm">{server?.name ?? (connection.port ? `tcp:${connection.host},${connection.port}` : `${connection.host}\\${connection.instanceName ?? ''}`)}</p><p className="mt-1 text-sm text-slate-500">SQL Server · {connection.authenticationType === 'windows' ? 'Windows Authentication' : 'SQL Server Authentication'}</p>{server && <p className="mt-1 text-xs text-slate-400">Phiên bản {server.version}</p>}</div></div>
        <span className={`rounded-full px-3 py-1 text-xs font-medium ${server ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-600'}`}>{server ? '● Đã kết nối' : 'Đã lưu · Chưa tải database'}</span>
      </div>
      <div className="mt-4 flex flex-wrap gap-3 border-t border-slate-100 pt-4"><button type="button" className="job-button" onClick={onConfigure}>Cấu hình kết nối</button><button type="button" className="job-button text-red-600" onClick={onDisconnect}>Ngắt kết nối</button></div>
      <p className="mt-3 text-xs text-slate-500">{server ? 'Metadata đã tải thành công. Trạng thái database sẽ được kiểm tra lại khi lưu job.' : 'Có thể cấu hình lại hoặc tải lại database từ kết nối đã lưu.'}</p>
    </div> : <button type="button" onClick={onConfigure} className="w-full rounded-xl border border-dashed border-blue-200 bg-blue-50/40 px-4 py-7 text-sm font-semibold text-blue-700 hover:bg-blue-50">+ Thêm kết nối SQL Server</button>}
    {error && <p className="mt-3 text-sm text-red-600">{error}</p>}
  </ConfigCard>;
}

