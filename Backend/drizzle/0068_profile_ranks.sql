-- The own ranks the game showed in its ranking window ([{type, scope, rank, points, change, grade, stats, at}]); own character only.
ALTER TABLE `player_profiles` ADD `ranks_json` text DEFAULT '[]' NOT NULL;
