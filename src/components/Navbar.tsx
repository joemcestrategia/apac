import Link from "next/link";

export default function Navbar() {
  return (
    <nav className="fixed top-0 left-0 right-0 z-50 glass">
      <div className="mx-auto max-w-7xl px-6 h-16 flex items-center justify-between">
        <Link href="/" className="flex items-center gap-2 font-bold text-xl">
          <span className="w-8 h-8 rounded-lg bg-accent flex items-center justify-center text-white text-sm font-extrabold">
            A
          </span>
          <span>Apac</span>
        </Link>

        <div className="hidden md:flex items-center gap-8 text-sm text-white/70">
          <Link href="#features" className="hover:text-white transition-colors">
            Features
          </Link>
          <Link href="#pricing" className="hover:text-white transition-colors">
            Pricing
          </Link>
          <Link href="#" className="hover:text-white transition-colors">
            Docs
          </Link>
          <Link href="#" className="hover:text-white transition-colors">
            Blog
          </Link>
        </div>

        <div className="flex items-center gap-3">
          <Link
            href="#"
            className="hidden sm:inline-flex text-sm text-white/70 hover:text-white transition-colors"
          >
            Sign in
          </Link>
          <Link
            href="#"
            className="inline-flex items-center justify-center h-9 px-5 rounded-lg bg-white text-surface text-sm font-semibold hover:bg-white/90 transition-all"
          >
            Get started
          </Link>
        </div>
      </div>
    </nav>
  );
}
