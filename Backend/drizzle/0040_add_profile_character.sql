-- Character window of the uploader's own character: the six main attributes and the ten lord values (id -> value),
-- the worn wing and wing skin (item ids) the active pet (species id) and its level. Older clients do not send them.
ALTER TABLE `player_profiles` ADD `attributes_json` text DEFAULT '{}' NOT NULL;
--> statement-breakpoint
ALTER TABLE `player_profiles` ADD `wing_id` integer;
--> statement-breakpoint
ALTER TABLE `player_profiles` ADD `wing_skin_id` integer;
--> statement-breakpoint
ALTER TABLE `player_profiles` ADD `active_pet` integer;
--> statement-breakpoint
ALTER TABLE `player_profiles` ADD `active_pet_level` integer;
