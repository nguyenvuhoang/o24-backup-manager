export function Logo() {
  return (
    <div className="flex items-center gap-2 text-xl font-bold">
      <svg
        aria-hidden="true"
        viewBox="0 0 32 32"
        width={28}
        height={28}
        className="shrink-0 text-blue-400"
        fill="none"
        stroke="currentColor"
        strokeWidth={1.8}
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <ellipse cx={13} cy={7} rx={9} ry={4} />
        <path d="M4 7v15c0 2.2 4 4 9 4M22 7v7M4 14c0 2.2 4 4 9 4h1" />
        <path d="M28 19a7 7 0 1 0 1 7M28 15v5h-5" />
      </svg>
      <span className="min-w-0">Backup Manager</span>
    </div>
  );
}
