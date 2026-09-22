-- Real incident (per the user, confirmed against Aahz's own Chat.log path "D:\Spiele\AION\Aion
-- Riftshade/Chat.log"): Aion Riftshade and Origin Aion share the exact same client fingerprint
-- "70.0.0.150:10241" (both reachable through the same gateway). Before this migration's companion
-- schema/code fix (0020, matching/merge.ts's upsertServer), that fingerprint alone was enough to
-- identify a `servers` row, so the moment Riftshade's first real upload arrived (uploader "Aahz",
-- upload id 457, 2026-09-15 16:34:50) it matched Origin Aion's own long-existing row by fingerprint
-- and overwrote its display_name to "Aion Riftshade" - silently relabeling Origin Aion's row AND
-- filing this Riftshade run's 7 new players/1 encounter under it. Restoring the row's name and
-- splitting the Riftshade data into a real row of its own. Confirmed via the players table that
-- all 7 names below are brand new (first_seen_at exactly matches the clobbering upload, none
-- existed under this fingerprint before), so this is a clean split, not a guess at which of an
-- already-mixed set belongs where.
UPDATE `servers` SET `display_name` = 'Origin Aion'
WHERE `fingerprint` = '70.0.0.150:10241' AND `display_name` = 'Aion Riftshade';
--> statement-breakpoint

INSERT INTO `servers` (`fingerprint`, `display_name`, `first_seen_at`)
VALUES ('70.0.0.150:10241', 'Aion Riftshade', '2026-09-15 16:34:50');
--> statement-breakpoint

UPDATE `players` SET `server_id` = (
  SELECT id FROM `servers` WHERE `fingerprint` = '70.0.0.150:10241' AND `display_name` = 'Aion Riftshade'
)
WHERE `server_id` = (
  SELECT id FROM `servers` WHERE `fingerprint` = '70.0.0.150:10241' AND `display_name` = 'Origin Aion'
)
AND `name` IN ('Aahz', 'Tecumseh', 'Orito', 'Painkillah', 'Yuri', 'Skaleria', 'Wind Spirit');
--> statement-breakpoint

UPDATE `encounters` SET `server_id` = (
  SELECT id FROM `servers` WHERE `fingerprint` = '70.0.0.150:10241' AND `display_name` = 'Aion Riftshade'
)
WHERE `server_id` = (
  SELECT id FROM `servers` WHERE `fingerprint` = '70.0.0.150:10241' AND `display_name` = 'Origin Aion'
)
AND `boss_id` = (SELECT id FROM `bosses` WHERE `name` = 'Brigade General Sheba')
AND `started_at` = '2026-09-15T16:28:47Z';
--> statement-breakpoint

UPDATE `uploads` SET `server_id` = (
  SELECT id FROM `servers` WHERE `fingerprint` = '70.0.0.150:10241' AND `display_name` = 'Aion Riftshade'
)
WHERE `uploader_reported_name` = 'Aahz'
AND `matched_encounter_id` = (
  SELECT id FROM `encounters`
  WHERE `boss_id` = (SELECT id FROM `bosses` WHERE `name` = 'Brigade General Sheba')
    AND `started_at` = '2026-09-15T16:28:47Z'
);
