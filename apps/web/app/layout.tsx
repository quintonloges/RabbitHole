import type { Metadata } from "next";
import { AppShell } from "@/components/app-shell";
import "./globals.css";

export const metadata: Metadata = { title: "RabbitHole — follow curiosity", description: "A private place to discover, read, and follow ideas." };

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en" data-scroll-behavior="smooth" suppressHydrationWarning><head><script dangerouslySetInnerHTML={{ __html: `(function(){try{var t=localStorage.getItem('rabbithole-theme')||'system';document.documentElement.dataset.theme=t}catch(e){}})()` }} /></head><body><AppShell>{children}</AppShell></body></html>;
}
