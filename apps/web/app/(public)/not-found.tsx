import Link from "next/link";
export default function NotFound() { return <div className="empty"><h1>Item unavailable</h1><p>This item is not in the public catalog.</p><Link prefetch={false} className="btn" href="/">Browse the catalog</Link></div>; }
