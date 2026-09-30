import { ConfigCard, FormField } from './form-elements';

export interface JobDetails { name: string; backupDirectory: string; retentionDays: number }
export function BasicInformationCard({ value, onChange, errors }: { value: JobDetails; onChange: (value: JobDetails) => void; errors: Record<string, string> }) {
  return <ConfigCard number={1} title="Thông tin cơ bản">
    <div className="grid gap-5 sm:grid-cols-[1fr_160px]">
      <FormField label="Tên job" name="name" required maxLength={200} placeholder="Tên backup job" value={value.name} onChange={e => onChange({ ...value, name: e.target.value })} error={errors.name} />
      <FormField label="Giữ file (ngày)" name="retentionDays" type="number" required min={1} step={1} value={Number.isNaN(value.retentionDays) ? '' : value.retentionDays} onChange={e => onChange({ ...value, retentionDays: e.target.valueAsNumber })} error={errors.retentionDays} />
      <div className="relative sm:col-span-2"><FormField label="Thư mục backup" name="backupDirectory" required style={{ paddingRight: 40 }} placeholder="Đường dẫn thư mục trên backend / SQL Server" value={value.backupDirectory} onChange={e => onChange({ ...value, backupDirectory: e.target.value })} error={errors.backupDirectory} hint="Backend tạo thư mục khi chạy backup. Dịch vụ SQL Server cần truy cập được cùng đường dẫn này." /><svg aria-hidden="true" className="pointer-events-none absolute top-10 right-3 h-5 w-5 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"><path d="M3 7V5a1 1 0 0 1 1-1h5l2 3h9a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7Z" /></svg></div>
    </div>
  </ConfigCard>;
}
