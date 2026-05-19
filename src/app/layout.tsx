import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Apac — Smart Compliance for European Companies",
  description:
    "Apac automates regulatory compliance, tax reporting, and entity management for businesses operating across the EU. Stay ahead of every deadline.",
  openGraph: {
    title: "Apac — Smart Compliance for European Companies",
    description:
      "Automate regulatory compliance, tax reporting, and entity management across the EU.",
    type: "website",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link
          rel="preconnect"
          href="https://fonts.gstatic.com"
          crossOrigin="anonymous"
        />
        <link
          href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800;900&family=JetBrains+Mono:wght@400;500&display=swap"
          rel="stylesheet"
        />
      </head>
      <body className="font-sans">{children}</body>
    </html>
  );
}
