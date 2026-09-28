import type { NextConfig } from "next";

// Все запросы к API идут через тот же origin (/api/*): cookie refresh-токена остаются first-party.
const apiUrl = process.env.API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

const nextConfig: NextConfig = {
  output: "standalone",
  poweredByHeader: false,
  async rewrites() {
    return [
      { source: "/api/:path*", destination: `${apiUrl}/api/:path*` },
      { source: "/openapi/:path*", destination: `${apiUrl}/openapi/:path*` },
    ];
  },
};

export default nextConfig;
