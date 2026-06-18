import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "OMyFish — Fish Identification",
  description: "Identify fish species using AI",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body className="min-h-screen bg-gray-50 text-gray-900 antialiased">
        <header className="bg-white border-b shadow-sm">
          <div className="max-w-5xl mx-auto px-4 py-3 flex items-center gap-3">
            <span className="text-2xl">🐟</span>
            <span className="text-xl font-bold tracking-tight text-blue-700">OMyFish</span>
          </div>
        </header>
        <main className="max-w-5xl mx-auto px-4 py-8">{children}</main>
      </body>
    </html>
  );
}
