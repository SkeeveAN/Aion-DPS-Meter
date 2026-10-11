-- More of NC's official character page: combat power, the website's item level, the legion NC files the character under and his level.
-- The gear, skills and Daevanion board counts go into player_profiles (source 'nc'), where the profile page already reads them.
ALTER TABLE `player_nc_character` ADD `combat_power` integer;
--> statement-breakpoint
ALTER TABLE `player_nc_character` ADD `item_level` integer;
--> statement-breakpoint
ALTER TABLE `player_nc_character` ADD `nc_guild` text;
--> statement-breakpoint
ALTER TABLE `player_nc_character` ADD `nc_level` integer;
