export type Notice = { kind: 'success' | 'error' | 'info'; message: string };
export function Notification({ notice, onDismiss }: { notice: Notice | null; onDismiss?: () => void }) {
  if (!notice) return null;
  const colors = { success: 'border-emerald-200 bg-emerald-50 text-emerald-800', error: 'border-red-200 bg-red-50 text-red-800', info: 'border-blue-200 bg-blue-50 text-blue-800' };
  return <div role={notice.kind === 'error' ? 'alert' : 'status'} className={`flex items-start gap-3 rounded-xl border px-4 py-3 text-sm ${colors[notice.kind]}`}>
    <span className="min-w-0 flex-1">{notice.message}</span>
    {onDismiss && <button type="button" aria-label="Đóng thông báo" onClick={onDismiss}>×</button>}
  </div>;
}
