import { and, eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { bossMechanics, bossNpcIds, bosses, instances } from "../db/schema.js";
import { loadAion2Content, type Aion2Mechanic } from "./aion2Content.js";

/**
 * Upserts src/data/aion2/*.json into the database. Idempotent and additive: rows are matched by
 * (game, name) for instances, (instance, name) for bosses and (owner, key) for mechanics; nothing
 * is ever deleted (a boss that vanished from the dataset keeps its uploaded encounters). Hand-set
 * DB state that the JSON does not know about (isTrashMob, isSolo, lootRules, npcNameAliases) is
 * left alone. Runs at every deploy after db:migrate - see deploy/install-backend.sh.
 */
export function syncAion2Content(): { instances: number; bosses: number; npcIds: number; mechanics: number } {
  const content = loadAion2Content();
  const counts = { instances: 0, bosses: 0, npcIds: 0, mechanics: 0 };

  const instanceIdByKey = new Map<string, number>();
  for (const instance of content.instances) {
    const existing = db
      .select({ id: instances.id })
      .from(instances)
      .where(and(eq(instances.game, "aion2"), eq(instances.name, instance.name.en)))
      .get();
    const values = { nameEn: instance.name.en, slug: instance.slug, category: instance.category, sortOrder: instance.sortOrder, source: "derived" as const };
    if (existing) {
      db.update(instances).set(values).where(eq(instances.id, existing.id)).run();
      instanceIdByKey.set(instance.key, existing.id);
    } else {
      const inserted = db.insert(instances).values({ name: instance.name.en, game: "aion2", ...values }).run();
      instanceIdByKey.set(instance.key, Number(inserted.lastInsertRowid));
    }
    counts.instances++;
  }

  const bossIdByKey = new Map<string, number>();
  for (const boss of content.bosses) {
    const instanceId = instanceIdByKey.get(boss.instanceKey);
    if (instanceId === undefined) {
      console.warn(`boss ${boss.key}: unknown instance ${boss.instanceKey}, skipped`);
      continue;
    }
    const existing = db
      .select({ id: bosses.id })
      .from(bosses)
      .where(and(eq(bosses.instanceId, instanceId), eq(bosses.name, boss.name.en)))
      .get();
    let bossId: number;
    if (existing) {
      db.update(bosses).set({ nameEn: boss.name.en, slug: boss.slug }).where(eq(bosses.id, existing.id)).run();
      bossId = existing.id;
    } else {
      const inserted = db.insert(bosses).values({ instanceId, name: boss.name.en, nameEn: boss.name.en, slug: boss.slug, source: "derived", npcNameAliases: [] }).run();
      bossId = Number(inserted.lastInsertRowid);
    }
    bossIdByKey.set(boss.key, bossId);
    counts.bosses++;

    for (const npcId of boss.npcIds) {
      const owner = db.select({ bossId: bossNpcIds.bossId }).from(bossNpcIds).where(eq(bossNpcIds.npcId, npcId)).get();
      if (!owner) {
        db.insert(bossNpcIds).values({ bossId, npcId }).run();
        counts.npcIds++;
      } else if (owner.bossId !== bossId) {
        console.warn(`npc ${npcId}: already belongs to boss ${owner.bossId}, not moved to ${bossId}`);
      }
    }
  }

  for (const mechanic of content.mechanics) {
    const owner = ownerOf(mechanic, bossIdByKey, instanceIdByKey);
    if (!owner) {
      console.warn(`mechanic ${mechanic.key}: unknown owner ${mechanic.bossKey ?? mechanic.instanceKey}, skipped`);
      continue;
    }
    const where = owner.bossId !== null ? and(eq(bossMechanics.bossId, owner.bossId), eq(bossMechanics.key, mechanic.key)) : and(eq(bossMechanics.instanceId, owner.instanceId!), eq(bossMechanics.key, mechanic.key));
    const values = {
      sortOrder: mechanic.sortOrder,
      triggerType: mechanic.trigger.type,
      triggerPct: mechanic.trigger.pct ?? null,
      triggerLabel: mechanic.trigger.label,
      severity: mechanic.severity,
      action: mechanic.action,
      detail: mechanic.detail,
      positionSheet: mechanic.positionSheet ?? null,
    };
    const existing = db.select({ id: bossMechanics.id }).from(bossMechanics).where(where).get();
    if (existing) {
      db.update(bossMechanics).set(values).where(eq(bossMechanics.id, existing.id)).run();
    } else {
      db.insert(bossMechanics).values({ bossId: owner.bossId, instanceId: owner.instanceId, key: mechanic.key, ...values }).run();
    }
    counts.mechanics++;
  }

  return counts;
}

function ownerOf(
  mechanic: Aion2Mechanic,
  bossIdByKey: Map<string, number>,
  instanceIdByKey: Map<string, number>,
): { bossId: number | null; instanceId: number | null } | null {
  if (mechanic.bossKey) {
    const bossId = bossIdByKey.get(mechanic.bossKey);
    return bossId === undefined ? null : { bossId, instanceId: null };
  }
  if (mechanic.instanceKey) {
    const instanceId = instanceIdByKey.get(mechanic.instanceKey);
    return instanceId === undefined ? null : { bossId: null, instanceId };
  }
  return null;
}

if (process.argv[1] && /syncAion2\.(ts|js)$/.test(process.argv[1])) {
  const counts = syncAion2Content();
  console.log(`Aion 2 content synced: ${counts.instances} instances, ${counts.bosses} bosses, ${counts.npcIds} new npc ids, ${counts.mechanics} mechanics.`);
}
