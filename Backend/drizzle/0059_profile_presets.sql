-- The skill presets of a player's own character (the server sends only the active one; the client keeps the others).
ALTER TABLE `player_profiles` ADD `presets_json` text DEFAULT '[]' NOT NULL;
