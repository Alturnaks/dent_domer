export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-[radial-gradient(ellipse_at_top,_oklch(0.95_0.03_230),_transparent_60%)] p-4">
      <div className="w-full max-w-sm">{children}</div>
    </div>
  );
}
