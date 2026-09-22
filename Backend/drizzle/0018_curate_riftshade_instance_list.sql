-- Per the user: curate Aion Riftshade's own instance list down to the 9 they actually want tracked
-- - most of what 0010/0014/0016 mapped in was a wide/speculative net, never confirmed as instances
-- this particular server's players actually run. Only `server_catalog_instances` (the mapping) is
-- touched - the `instances`/`bosses` rows themselves stay, same "unmap, never delete" pattern as
-- backend/README.md's trash-mob/solo-boss guidance, so any already-uploaded encounters for these
-- keep working via their direct leaderboard links.
DELETE FROM `server_catalog_instances`
WHERE `server_catalog_id` = (SELECT id FROM `server_catalog` WHERE name = 'AionRiftshade')
  AND `instance_id` IN (
    SELECT id FROM `instances` WHERE name IN (
      'Aturam Sky Fortress',
      'Baruna Research Laboratory',
      'Beshmundirs Tempel',
      'Hall of Knowledge',
      'Jormungand''s Bridge',
      'Linkgate Foundry',
      'Lost Refuge',
      'Mantor',
      'Rentus-Basis',
      'Tahmes',
      'Tiamats Festung',
      'Stahlrose: Anlegestelle',
      'Stahlrose: Kabine',
      'Stahlrose: Deck',
      'Ophidan Bridge',
      'Danuar Sanctuary'
    )
  );
--> statement-breakpoint

-- New order for the 9 that remain (per the user): Lost Rentus Base, Tiamats Unterschlupf,
-- Sauro-Kriegsdepot, Katalamize, Stahlmauerbastion, Schutzturm der Ruhn, Ruhnadium, Tiamat's Hidden
-- Space, Makarna. `instances.sort_order` is UNSCOPED (shared with every server, see schema.ts's own
-- remarks) - the middle 5 above already carry sort_order 4/5/6/9/10 from migration 0014 in exactly
-- this same relative order and are also Origin Aion/EuroAion's shared list (confirmed via GET
-- /api/instances?serverCatalogId=2 and =3), so they're deliberately left untouched here rather than
-- renumbered, to avoid reordering those two servers' own lists as a side effect. Only the 4
-- Riftshade-exclusive instances below (confirmed via the same endpoint to be mapped to no other
-- server) get new sort_order values, chosen to slot immediately before/after that untouched block.
UPDATE `instances` SET `sort_order` = 1 WHERE `name` = 'Lost Rentus Base';
--> statement-breakpoint
UPDATE `instances` SET `sort_order` = 2 WHERE `name` = 'Tiamats Unterschlupf';
--> statement-breakpoint
UPDATE `instances` SET `sort_order` = 11 WHERE `name` = 'Tiamat''s Hidden Space';
--> statement-breakpoint
UPDATE `instances` SET `sort_order` = 12 WHERE `name` = 'Makarna';
