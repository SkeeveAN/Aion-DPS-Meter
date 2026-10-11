-- How a character's data is kept fresh (see refreshPolicy.ts): the signature of what the last look found (combat power, level, item level,
-- legion, title, main attributes, Daevanion progress), how many looks in a row found nothing new, and the time of the last complete fetch.
ALTER TABLE `player_nc_character` ADD `probe_json` text;
--> statement-breakpoint
ALTER TABLE `player_nc_character` ADD `unchanged_count` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `player_nc_character` ADD `full_at` integer;
