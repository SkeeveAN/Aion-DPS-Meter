-- Result of the automatic check of a player against NC's public character search (see ncMatch.ts): which server and faction the one matching
-- character has. One row per player that was checked; 'client' marks a server the player's own client named.
CREATE TABLE `player_nc_match` (
	`player_id` integer PRIMARY KEY NOT NULL,
	`status` text NOT NULL,
	`nc_server_id` integer,
	`nc_race` integer,
	`via` text,
	`candidates` integer DEFAULT 0 NOT NULL,
	`checked_at` integer NOT NULL,
	`next_try_at` integer NOT NULL,
	FOREIGN KEY (`player_id`) REFERENCES `players`(`id`) ON UPDATE no action ON DELETE cascade
);
