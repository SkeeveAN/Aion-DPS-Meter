-- A fight the uploader joined late sees only a part of the boss's health as its "maximum" and used to pass as a whole kill. A fight whose
-- highest health reading stays below the boss's real maximum is a partial fight: stored and shown, but kept out of every ranking and average.
-- boss_spawn_max_hp = the max health from the spawn frame as the client sent it (raw), boss_real_max_hp = the maximum the server judged against,
-- hp_seen_share = highest reading / real maximum (0..1).
ALTER TABLE `encounters` ADD `is_partial` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounters` ADD `boss_spawn_max_hp` integer;
--> statement-breakpoint
ALTER TABLE `encounters` ADD `boss_real_max_hp` integer;
--> statement-breakpoint
ALTER TABLE `encounters` ADD `hp_seen_share` real;
--> statement-breakpoint
-- The real health per boss NPC id: seeded from the content file (see bossHp.ts), kept up to date from the spawn values of incoming uploads.
CREATE TABLE `boss_hp_reference` (
	`npc_id` integer PRIMARY KEY NOT NULL,
	`base_hp` integer NOT NULL,
	`party_scaled` integer DEFAULT 0 NOT NULL,
	`source` text NOT NULL,
	`samples` integer DEFAULT 0 NOT NULL,
	`updated_at` text NOT NULL
);
--> statement-breakpoint
CREATE INDEX `encounters_is_partial_idx` ON `encounters` (`is_partial`);
