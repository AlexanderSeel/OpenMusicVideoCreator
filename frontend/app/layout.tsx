import type { Metadata } from "next";
import type { ReactNode } from "react";
import "./globals.css";
import "./library.css";
import "./director.css";
import "./keyframes.css";
import "./generation.css";

export const metadata: Metadata = {
  title: "OpenMusicVideoCreator",
  description: "AI music-video creation studio",
};

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
