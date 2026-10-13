import { buildServer } from "./server.js";
import { env } from "./env.js";
import { startStatusProbe } from "./status/serverStatus.js";
import { startEnrichSweep, startMatchSweep } from "./ncMatch.js";
import { startNcGateSweep } from "./ncGate.js";
import { startReferenceCheck } from "./ncReference.js";
import { startAnnouncements } from "./announcements.js";
import { startPatchnotes } from "./patchnotes.js";
import { startTwitchDrops } from "./twitchDrops.js";

async function main() {
  const app = await buildServer();
  await app.listen({ port: env.PORT, host: env.HOST });
  startStatusProbe();
  startMatchSweep();
  startEnrichSweep();
  startNcGateSweep();
  startReferenceCheck();
  startAnnouncements();
  startPatchnotes();
  startTwitchDrops();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
