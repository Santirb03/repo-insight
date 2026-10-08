import type { Metadata } from "next";
import "./globals.css";
export const metadata: Metadata = {
  title: "RepoInsight — Entiende tu proyecto",
  description:
    "Explora las tecnologías, la arquitectura y los hallazgos de tu repositorio a partir de un ZIP.",
};
export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="es">
      <body>{children}</body>
    </html>
  );
}
