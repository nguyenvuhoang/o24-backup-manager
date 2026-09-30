import type { NextConfig } from 'next';
const backend = process.env.BACKEND_URL ?? 'http://127.0.0.1:5088';
const config: NextConfig = {
  output: 'standalone',
  async rewrites() {
    return [
      { source: '/api/:path*', destination: `${backend}/api/:path*` },
      { source: '/hubs/:path*', destination: `${backend}/hubs/:path*` }
    ];
  }
};
export default config;
