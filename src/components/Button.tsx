const variants = {
  primary:
    "bg-white text-surface hover:bg-white/90 shadow-lg shadow-white/10",
  secondary:
    "glass hover:bg-white/10 text-white",
  accent:
    "bg-accent text-white hover:bg-accent/90 shadow-lg shadow-accent/20",
};

interface ButtonProps {
  children: React.ReactNode;
  variant?: keyof typeof variants;
  className?: string;
}

export default function Button({
  children,
  variant = "primary",
  className = "",
}: ButtonProps) {
  return (
    <button
      className={`inline-flex items-center justify-center h-11 px-6 rounded-xl text-sm font-semibold transition-all ${variants[variant]} ${className}`}
    >
      {children}
    </button>
  );
}
