-- URL slug per player ("aahz-kaisinel"); filled by db/backfill.ts and at insert, never reused.
ALTER TABLE `players` ADD `slug` text;
--> statement-breakpoint
CREATE UNIQUE INDEX `players_slug_idx` ON `players` (`slug`);
