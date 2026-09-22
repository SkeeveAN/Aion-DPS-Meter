-- Per the user: AP/Kinah/EXP/loot are never uploaded, only damage AND heal - so ap_total is
-- dropped outright rather than just stopping writes to it (an unused nullable column nobody
-- populates anymore is dead weight, not a safer middle ground). total_healing/hps get a real
-- literal default (0), unlike the server_id columns in the previous migration: those needed a
-- nullable-then-backfill dance because there was no safe default to point a foreign key at, but
-- "no healing recorded" is a perfectly correct value for every row that predates this column, so a
-- single ADD COLUMN ... NOT NULL DEFAULT 0 is enough.
ALTER TABLE `encounter_participants` ADD `total_healing` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounter_participants` ADD `hps` real DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounter_participants` DROP COLUMN `ap_total`;
--> statement-breakpoint
-- Distinguishes a damage skill-usage row from a heal skill-usage row in the one shared
-- encounter_skill_usage table (see schema.ts's own remarks on why this isn't a separate table).
-- Existing rows predate heal tracking entirely, so they are all damage rows - default false is
-- exactly right for them, not a placeholder to fix up later.
ALTER TABLE `encounter_skill_usage` ADD `is_heal` integer DEFAULT false NOT NULL;
