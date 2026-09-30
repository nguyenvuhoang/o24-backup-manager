'use client';

import { Logo } from './logo';

const items = [
  ['overview', 'Tổng quan'],
  ['create', 'Tạo backup job'],
  ['history', 'Lịch sử'],
  ['settings', 'Cấu hình'],
];

export function Sidebar({
  view,
  setView,
}: {
  view: string;
  setView: (v: string) => void;
}) {
  return (
    <aside
      className="
        flex w-full shrink-0 flex-col
        border-r border-[#26344d]
        bg-[#18243b]
        p-6 text-white
        md:min-h-screen md:w-56
        xl:w-64
      "
    >
      <div className="mb-4 md:mb-10">
        <Logo />

        <div className="mt-1 text-xs text-[#8f9bb0]">
          O24 Backup Database
        </div>
      </div>

      <nav className="flex flex-wrap gap-1 md:block md:space-y-1.5">
        {items.map(([id, label]) => {
          const active = view === id;

          return (
            <button
              key={id}
              onClick={() => setView(id)}
              className={[
                'rounded-xl px-4 py-3 text-left text-sm transition-all duration-150',
                'md:w-full',
                active
                  ? 'bg-[#4f73c8] text-white shadow-[0_4px_12px_rgba(34,54,94,0.18)]'
                  : 'text-[#c1c9d6] hover:bg-white/[0.055] hover:text-white',
              ].join(' ')}
            >
              {label}
            </button>
          );
        })}
      </nav>

      <div
        className="
          mt-auto hidden items-center
          rounded-xl border border-white/[0.08]
          bg-white/[0.025]
          p-3 text-xs text-[#8f9bb0]
          md:flex
        "
      >
        <span className="mr-2.5 inline-block h-2 w-2 rounded-full bg-[#63b89a]" />

        Local agent
      </div>
    </aside>
  );
}