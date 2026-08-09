"use client";

import { useEffect, useState } from "react";
import { getSystemVersion } from "@/src/api/client";

type BackendState =
  | { kind: "loading" }
  | { kind: "ready"; applicationName: string; version: string; environment: string }
  | { kind: "error"; message: string };

export default function HomePage() {
  const [backend, setBackend] = useState<BackendState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    getSystemVersion(controller.signal)
      .then((result) => {
        setBackend({ kind: "ready", ...result });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setBackend({
          kind: "error",
          message: error instanceof Error ? error.message : "Backend is unavailable.",
        });
      });

    return () => controller.abort();
  }, []);

  return (
    <main>
      <section className="panel" aria-labelledby="studio-title">
        <p className="eyebrow">Foundation build</p>
        <h1 id="studio-title">Open Music Video Creator</h1>
        <p className="lead">
          The application shell is connected through a typed API contract. Product workflows will be added as complete vertical blocks from the repository plan.
        </p>

        <dl className="status" aria-live="polite">
          <div>
            <dt>Frontend</dt>
            <dd>Next.js 16</dd>
          </div>
          <div>
            <dt>Backend</dt>
            <dd>
              {backend.kind === "loading" && "Connecting…"}
              {backend.kind === "ready" && `${backend.applicationName} ${backend.version}`}
              {backend.kind === "error" && "Unavailable"}
            </dd>
          </div>
          <div>
            <dt>Environment</dt>
            <dd>{backend.kind === "ready" ? backend.environment : "—"}</dd>
          </div>
        </dl>

        {backend.kind === "error" ? <p className="lead">{backend.message}</p> : null}
      </section>
    </main>
  );
}
