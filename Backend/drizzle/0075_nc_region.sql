-- Korea and Taiwan share the server ids 1001-1018 / 2001-2018: the region NC's search found a verified character in (null = global, or the region of the server label).
ALTER TABLE `player_nc_match` ADD `nc_region` text;
