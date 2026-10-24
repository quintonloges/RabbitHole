import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = { title: "RabbitHole", description: "Find articles, papers, books, and music. Save what interests you and follow the connections." };

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en" data-scroll-behavior="smooth" suppressHydrationWarning><head><script dangerouslySetInnerHTML={{ __html: `(function(){try{var t=localStorage.getItem('rabbithole-theme')||'system';document.documentElement.dataset.theme=t}catch(e){}})()` }} /></head><body>{children}</body></html>;
}
