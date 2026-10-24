"use client";
export default function PublicError({ reset }: { reset: () => void }) { return <div className="empty" role="alert"><h1>The catalog is temporarily unavailable</h1><p>Please try again in a moment.</p><button className="btn" onClick={reset}>Try again</button><a className="btn" href="/">Back to the catalog</a></div>; }
