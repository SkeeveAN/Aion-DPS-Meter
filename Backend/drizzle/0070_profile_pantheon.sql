-- The own Pantheon (decorations set up per preset, and the owned ones) as the login sent it ({presets: [{preset, slots: [{slot, id}]}], owned: [id]}); own character only.
ALTER TABLE `player_profiles` ADD `pantheon_json` text DEFAULT '{}' NOT NULL;
