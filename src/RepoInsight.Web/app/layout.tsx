import type { Metadata } from "next";
import "./globals.css";
export const metadata: Metadata = {
  title: "RepoInsight — Understand your codebase",
  description:
    "Explore repository technologies, architecture, and findings from a single ZIP upload.",
};
export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
