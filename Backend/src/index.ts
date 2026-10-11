import { buildServer } from "./server.js";
import { env } from "./env.js";
import { startStatusProbe } from "./status/serverStatus.js";
import { startEnrichSweep, startMatchSweep } from "./ncMatch.js";
import { startReferenceCheck } from "./ncReference.js";

async function main() {
  const app = await buildServer();
  await app.listen({ port: env.PORT, host: env.HOST });
  startStatusProbe();
  startMatchSweep();
  startEnrichSweep();
  startReferenceCheck();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
