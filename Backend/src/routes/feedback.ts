import { randomBytes } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { env } from "../env.js";

// Largest encrypted recording accepted, as base64 text (~24 MB of ciphertext). The client keeps its
// capture buffer well below this; the cap only protects the server from a hand-made request.
const MAX_RECORDING_B64 = 32 * 1024 * 1024;

const feedbackSchema = z.object({
  type: z.enum(["feature", "issue", "improvement"]),
  name: z.string().trim().min(1).max(80),
  email: z.string().trim().max(200).email().optional().or(z.literal("")),
  message: z.string().trim().min(10).max(5000),
  source: z.enum(["web", "client"]),
  clientVersion: z.string().max(40).optional(),
  lang: z.string().max(8).optional(),
  // Encrypted on the user's machine (see Client/Feedback/FeedbackCrypto.cs) - the server only ever
  // forwards the ciphertext and cannot read it.
  recording: z.string().max(MAX_RECORDING_B64).regex(/^[A-Za-z0-9+/=]+$/).optional(),
  // Honeypot: a real visitor never sees or fills this field.
  website: z.string().max(200).optional(),
});

const TYPE_LABEL = { feature: "feature", issue: "bug", improvement: "enhancement" } as const;
const TYPE_TITLE = { feature: "Feature", issue: "Issue", improvement: "Improvement" } as const;

/** User text goes into a public issue: no pings, no mentions of other issues, no raw HTML. */
function neutralise(text: string): string {
  return text.replace(/@/g, "@​").replace(/#(\d)/g, "#​$1").replace(/</g, "&lt;");
}

async function github(path: string, init: { method: string; body: unknown }): Promise<Response> {
  return fetch(`https://api.github.com${path}`, {
    method: init.method,
    headers: {
      authorization: `Bearer ${env.GITHUB_TOKEN}`,
      accept: "application/vnd.github+json",
      "x-github-api-version": "2022-11-28",
      "user-agent": "aiondps-feedback",
      "content-type": "application/json",
    },
    body: JSON.stringify(init.body),
  });
}

async function putFile(repo: string, path: string, content: Buffer): Promise<void> {
  const res = await github(`/repos/${repo}/contents/${path}`, {
    method: "PUT",
    body: { message: `feedback: ${path}`, content: content.toString("base64") },
  });
  if (!res.ok) {
    throw new Error(`GitHub contents ${res.status}: ${(await res.text()).slice(0, 300)}`);
  }
}

export async function feedbackRoutes(app: FastifyInstance) {
  // Path stays under /api/feedback; the rate limiter in server.ts keys on that prefix.
  app.post(
    "/api/feedback",
    { bodyLimit: 40 * 1024 * 1024, config: { rateLimit: { max: 5, timeWindow: "1 hour" } } },
    async (request, reply) => {
      const parsed = feedbackSchema.safeParse(request.body);
      if (!parsed.success) {
        return reply.status(400).send({ error: "invalid_payload", details: parsed.error.flatten().fieldErrors });
      }
      const f = parsed.data;
      if (f.website) {
        // Bot: pretend success so it learns nothing.
        return reply.send({ status: "ok" });
      }
      if (!env.GITHUB_TOKEN || !env.GITHUB_ISSUES_REPO) {
        return reply.status(503).send({ error: "feedback_not_configured" });
      }

      const id = `${new Date().toISOString().slice(0, 10)}-${randomBytes(4).toString("hex")}`;
      try {
        // Private data first: contact address and the encrypted recording never go into the public issue.
        let hasPrivate = false;
        if (env.GITHUB_DATA_REPO) {
          const meta = { id, receivedAt: new Date().toISOString(), type: f.type, source: f.source, name: f.name, email: f.email || null, clientVersion: f.clientVersion ?? null, lang: f.lang ?? null, message: f.message };
          await putFile(env.GITHUB_DATA_REPO, `feedback/${id}/meta.json`, Buffer.from(JSON.stringify(meta, null, 2)));
          if (f.recording) {
            await putFile(env.GITHUB_DATA_REPO, `feedback/${id}/recording.bin.enc`, Buffer.from(f.recording, "base64"));
          }
          hasPrivate = true;
        }

        const firstLine = f.message.split("\n")[0]!.slice(0, 70);
        const body = [
          neutralise(f.message),
          "",
          "---",
          `From: ${neutralise(f.name)} · via ${f.source}${f.clientVersion ? ` ${f.clientVersion}` : ""}${f.lang ? ` · ${f.lang}` : ""}`,
          `Feedback ID: \`${id}\``,
          hasPrivate ? `Contact address${f.recording ? " and encrypted recording" : ""} are stored privately under this ID.` : "",
        ].join("\n");
        const res = await github(`/repos/${env.GITHUB_ISSUES_REPO}/issues`, {
          method: "POST",
          body: { title: `[${TYPE_TITLE[f.type]}] ${neutralise(firstLine)}`, body, labels: [TYPE_LABEL[f.type], "from-app"] },
        });
        if (!res.ok) {
          throw new Error(`GitHub issues ${res.status}: ${(await res.text()).slice(0, 300)}`);
        }
        const issue = (await res.json()) as { html_url: string; number: number };
        return reply.send({ status: "ok", issueUrl: issue.html_url, number: issue.number });
      } catch (err) {
        app.log.error(err, "feedback forwarding failed");
        return reply.status(502).send({ error: "forward_failed" });
      }
    },
  );
}
