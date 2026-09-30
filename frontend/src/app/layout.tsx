import './globals.css';
import { Quicksand } from 'next/font/google';

const quicksand = Quicksand({
  subsets: ['latin', 'vietnamese'],
  display: 'swap',
  variable: '--font-quicksand',
});
export const metadata = { title: 'Backup Manager', description: 'Database backup control center' };
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="vi" className={quicksand.variable}>
      {/* Browser extensions can add body attributes (e.g. ColorZilla's cz-shortcut-listen).
          Tolerate that boundary only; descendants still get normal hydration checks. */}
      <body className="font-sans" suppressHydrationWarning>{children}</body>
    </html>
  );
}
