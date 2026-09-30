'use client';
import { useEffect, useId, useRef, type ReactNode } from 'react';

export function Modal({ title, subtitle, onClose, children }: { title: string; subtitle: string; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const subtitleId = useId();
  useEffect(() => {
    const dialog = ref.current!;
    const oldOverflow = document.body.style.overflow;
    dialog.showModal();
    document.body.style.overflow = 'hidden';
    return () => { dialog.close(); document.body.style.overflow = oldOverflow; };
  }, []);
  return <dialog ref={ref} aria-labelledby={titleId} aria-describedby={subtitleId} onCancel={e => { e.preventDefault(); onClose(); }} className="connection-dialog">
    <header className="flex items-start justify-between gap-4 border-b border-slate-200 p-5 sm:p-6">
      <div><h2 id={titleId} className="text-xl font-semibold">{title}</h2><p id={subtitleId} className="mt-1 text-sm text-slate-500">{subtitle}</p></div>
      <button type="button" onClick={onClose} aria-label="Đóng cấu hình kết nối" className="rounded-lg px-2 text-2xl text-slate-500 hover:bg-slate-100">×</button>
    </header>
    {children}
  </dialog>;
}
