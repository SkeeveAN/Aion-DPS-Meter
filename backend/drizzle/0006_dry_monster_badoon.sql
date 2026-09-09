ALTER TABLE `bosses` ADD `is_solo` integer DEFAULT false NOT NULL;--> statement-breakpoint
ALTER TABLE `bosses` ADD `loot_rules` text DEFAULT '[]' NOT NULL;