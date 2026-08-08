// Cloud-save sync function (source).
//
// Stores/returns progress snapshots in Netlify Blobs (store "saves"), keyed by
// the player's sync code (e.g. "WOOF-4821"). The game's SyncManager POSTs
// {code, data} whenever progress changes and GETs ?code= to restore.
//
// Saves are browsable in the Netlify dashboard: Project -> Blobs -> "saves" -
// each key is a player's code, each value JSON with updatedAt + a summary.
//
// This file is NOT deployed directly: `npm run build` in this folder bundles
// it (with @netlify/blobs) into
//   Assets/WebGLTemplates/RingSportItch/netlify/functions/sync.mjs
// which Unity copies into every web build, so the normal zip -> drag-drop
// deploy carries the function with zero extra steps. Re-run the build only
// when editing this file.

import { getStore } from "@netlify/blobs";

const CODE_RE = /^[A-Z]{2,10}-\d{4}$/;
const MAX_BYTES = 20000;

const csvCount = (csv) =>
  typeof csv === "string" && csv ? csv.split(",").filter(Boolean).length : 0;

export default async (req) => {
  const store = getStore("saves");

  if (req.method === "GET") {
    const url = new URL(req.url);
    const code = (url.searchParams.get("code") || "").trim().toUpperCase();
    if (!CODE_RE.test(code))
      return Response.json({ error: "bad code" }, { status: 400 });
    const save = await store.get(code, { type: "json" });
    if (!save) return Response.json({ error: "not found" }, { status: 404 });
    return Response.json(save);
  }

  if (req.method === "POST") {
    let body = null;
    try {
      body = await req.json();
    } catch {}
    const code =
      body && typeof body.code === "string" ? body.code.trim().toUpperCase() : "";
    const data = body && body.data;
    if (!CODE_RE.test(code) || !data || typeof data !== "object")
      return Response.json({ error: "bad request" }, { status: 400 });
    if (JSON.stringify(data).length > MAX_BYTES)
      return Response.json({ error: "too large" }, { status: 413 });

    await store.setJSON(code, {
      updatedAt: new Date().toISOString(),
      // Human-readable at-a-glance line for the Blobs dashboard
      summary: {
        highScore: data.highScore | 0,
        hats: csvCount(data.hatsUnlocked),
        notes: csvCount(data.notesUnlocked),
      },
      data,
    });
    return Response.json({ ok: true });
  }

  return new Response("Method Not Allowed", { status: 405 });
};
