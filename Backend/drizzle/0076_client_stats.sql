-- Combat power and item level reported by a client (legion list of the login) are newer than NC's page: when they were reported (null = NC's values).
ALTER TABLE `player_nc_character` ADD `client_stats_at` integer;
