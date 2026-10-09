import { buildServer } from "./server.js";
import { env } from "./env.js";
import { startStatusProbe } from "./status/serverStatus.js";

async function main() {
  const app = await buildServer();
  await app.listen({ port: env.PORT, host: env.HOST });
  startStatusProbe();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
