-- NA East has a ninth server per faction (Hithanya 1109, Nemon 2109, announced 2026-10-09): the rows filed under "Name [id]" get their region label,
-- and the players on them are checked against NC again (without a region the check never ran).
UPDATE `servers` SET `fingerprint` = 'aion2:na-east-hithanya', `display_name` = 'NA East - Hithanya'
  WHERE `fingerprint` = 'aion2:hithanya-1109' AND NOT EXISTS (SELECT 1 FROM `servers` WHERE `fingerprint` = 'aion2:na-east-hithanya');
--> statement-breakpoint
UPDATE `servers` SET `fingerprint` = 'aion2:na-east-nemon', `display_name` = 'NA East - Nemon'
  WHERE `fingerprint` = 'aion2:nemon-2109' AND NOT EXISTS (SELECT 1 FROM `servers` WHERE `fingerprint` = 'aion2:na-east-nemon');
--> statement-breakpoint
UPDATE `player_nc_match` SET `next_try_at` = 0
  WHERE `status` <> 'verified' AND `player_id` IN (SELECT `id` FROM `players` WHERE `server_id` IN (SELECT `id` FROM `servers` WHERE `display_name` IN ('NA East - Hithanya', 'NA East - Nemon')));
