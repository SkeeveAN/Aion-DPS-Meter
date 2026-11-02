import { buildServer } from "./server.js";
import { env } from "./env.js";
import { startStatusProbe } from "./status/serverStatus.js";
import { startEnrichSweep, startMatchSweep } from "./ncMatch.js";
import { startNcGateSweep } from "./ncGate.js";
import { startCrowdSweep } from "./crowdResolve.js";
import { startReferenceCheck } from "./ncReference.js";
import { startAnnouncements } from "./announcements.js";
import { startPatchnotes } from "./patchnotes.js";
import { startTwitchDrops } from "./twitchDrops.js";
import { startMatchmaking } from "./serverMatchmakingDb.js";
import { dataManifest } from "./dataPack.js";

async function main() {
  const app = await buildServer();
  await app.listen({ port: env.PORT, host: env.HOST });
  startStatusProbe();
  startMatchSweep();
  startEnrichSweep();
  startNcGateSweep();
  startCrowdSweep(app.log);
  startReferenceCheck();
  startAnnouncements();
  startPatchnotes();
  startTwitchDrops();
  startMatchmaking();
  // The list of the data files is built (walking thousands of files) shortly after the start, not by the first client that asks.
  setTimeout(() => {
    try {
      dataManifest();
    } catch (err) {
      app.log.warn({ err: String(err) }, "data manifest could not be built");
    }
  }, 15_000).unref();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
