-- Worn titles, pet circles and (for other players) the activated node count per Daevanion board, as read from the game's character window.
ALTER TABLE `player_profiles` ADD `titles_json` text DEFAULT '[]' NOT NULL;
--> statement-breakpoint
ALTER TABLE `player_profiles` ADD `pets_json` text DEFAULT '[]' NOT NULL;
--> statement-breakpoint
ALTER TABLE `player_profiles` ADD `board_counts_json` text DEFAULT '[]' NOT NULL;
