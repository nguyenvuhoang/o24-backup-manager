import type { InputHTMLAttributes, ReactNode } from 'react';

export function FormField({ label, error, hint, ...input }: InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string; hint?: string }) {
  return <label className="block text-sm font-semibold text-slate-700">
    {label}{input.required && <span className="ml-1 text-red-500">*</span>}
    <input {...input} aria-invalid={!!error} className="job-input mt-2" />
    {hint && <span className="mt-1.5 block text-xs font-normal text-slate-500">{hint}</span>}
    {error && <span className="mt-1 block text-xs font-normal text-red-600">{error}</span>}
  </label>;
}
export function ConfigCard({ number, title, subtitle, children }: { number: number; title: string; subtitle?: string; children: ReactNode }) {
  return <section className="panel overflow-hidden">
    <header className="flex gap-3 border-b border-slate-100 px-5 py-5 sm:px-6">
      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-blue-50 text-sm font-semibold text-blue-700">{number}</span>
      <div><h2 className="font-semibold">{title}</h2>{subtitle && <p className="mt-1 text-sm text-slate-500">{subtitle}</p>}</div>
    </header>
    <div className="p-5 sm:p-6">{children}</div>
  </section>;
}

