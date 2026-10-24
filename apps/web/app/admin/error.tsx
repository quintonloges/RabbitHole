"use client";
export default function AdminError({ reset }: { reset: () => void }) {
  return <main><div className="empty"><h1>Workspace unavailable</h1><p>Your session or the service could not be checked.</p><button className="btn" onClick={reset}>Try again</button><a className="btn" href="/admin/login">Sign in</a></div></main>;
}
