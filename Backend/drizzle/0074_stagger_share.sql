-- How much of a boss's stagger bar each participant brought down, and how far the bar fell in all (clients that match the bar's readings to the hits send it).
ALTER TABLE `encounter_participants` ADD `stagger` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounters` ADD `stagger_total` integer;
