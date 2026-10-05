-- Species knowledge of the pet window (own character only): level, progress and the analysed effects per species.
ALTER TABLE `player_profiles` ADD `species_json` text DEFAULT '[]' NOT NULL;
