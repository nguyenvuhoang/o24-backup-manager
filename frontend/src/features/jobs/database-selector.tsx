import { useRef, useEffect, useState } from 'react';
import type { DatabaseInfo } from '@/types/api';
import { filterDatabases, formatDatabaseSize, selectableNames } from '@/lib/database-selection';
import { ConfigCard } from './form-elements';

export function DatabaseSelector({ databases, selected, connected, onChange, error }: { databases: DatabaseInfo[]; selected: string[]; connected: boolean; onChange: (names: string[]) => void; error?: string }) {
  const [search, setSearch] = useState('');
  const header = useRef<HTMLInputElement>(null);
  const visible = filterDatabases(databases, search);
  const eligible = selectableNames(visible);
  const all = eligible.length > 0 && eligible.every(name => selected.includes(name));
  useEffect(() => { if (header.current) header.current.indeterminate = !all && eligible.some(name => selected.includes(name)); }, [all, eligible, selected]);
  function toggleVisible() { onChange(all ? selected.filter(name => !eligible.includes(name)) : [...new Set([...selected, ...eligible])]); }
  return <ConfigCard number={3} title="Chọn database" subtitle="Chọn các database cần backup">
    {!connected ? <div className="py-8 text-center text-sm text-slate-500">Kết nối SQL Server để tải danh sách database.</div> : <>
      <input type="search" aria-label="Tìm database" className="job-input mb-4 max-w-sm" placeholder="Tìm database..." value={search} onChange={e => setSearch(e.target.value)} />
      <div className="overflow-x-auto rounded-xl border border-slate-200">
        <table className="w-full min-w-[570px] text-left text-sm">
          <thead className="bg-slate-50 text-xs text-slate-500"><tr><th className="p-3"><input ref={header} type="checkbox" aria-label="Chọn tất cả database trong kết quả tìm kiếm" checked={all} disabled={!eligible.length} onChange={toggleVisible} /></th><th className="p-3">Database</th><th className="p-3">Trạng thái</th><th className="p-3 text-right">Kích thước</th><th className="p-3">Ngày tạo</th></tr></thead>
          <tbody>{visible.map(d => {
            const canSelect = d.status === 'ONLINE' && d.isAccessible;
            const colors = d.status === 'ONLINE' ? 'bg-emerald-50 text-emerald-700' : d.status === 'RESTORING' || d.status === 'RECOVERY_PENDING' ? 'bg-amber-50 text-amber-700' : 'bg-slate-100 text-slate-600';
            return <tr key={d.name} className={`border-t border-slate-100 ${selected.includes(d.name) ? 'bg-blue-50/40' : ''}`}>
              <td className="p-3"><input type="checkbox" aria-label={`Chọn ${d.name}`} checked={selected.includes(d.name)} disabled={!canSelect} onChange={e => onChange(e.target.checked ? [...selected, d.name] : selected.filter(name => name !== d.name))} /></td>
              <td className="p-3 font-medium">{d.name}{!d.isAccessible && <span className="mt-1 block text-xs font-normal text-slate-500">Không có quyền truy cập</span>}</td>
              <td className="p-3"><span className={`whitespace-nowrap rounded-full px-2.5 py-1 text-xs ${colors}`}>{d.status}</span></td>
              <td className="p-3 text-right tabular-nums text-slate-600">{formatDatabaseSize(d.sizeMb)}</td><td className="whitespace-nowrap p-3 text-slate-600">{d.createdAt ? new Date(d.createdAt).toLocaleDateString('vi-VN') : '—'}</td>
            </tr>;
          })}</tbody>
        </table>
        {!visible.length && <p className="p-8 text-center text-sm text-slate-500">{databases.length ? 'Không tìm thấy database phù hợp.' : 'Không có user database hiển thị với tài khoản này.'}</p>}
      </div>
      <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm"><span className="text-slate-500">Đã chọn <strong className="text-slate-800">{selected.length}/{databases.length}</strong> database</span><div className="flex gap-4"><button type="button" className="text-blue-700 disabled:text-slate-400" disabled={!databases.length} onClick={() => onChange(selectableNames(databases))}>Chọn tất cả</button><button type="button" className="text-slate-600" onClick={() => onChange([])}>Bỏ chọn</button></div></div>
      <p className="mt-3 text-xs text-slate-400">System databases được loại bỏ. Chỉ chọn database ONLINE và có quyền truy cập; “—” là metadata không khả dụng.</p>
    </>}
    {error && <p className="mt-3 text-sm text-red-600">{error}</p>}
  </ConfigCard>;
}
