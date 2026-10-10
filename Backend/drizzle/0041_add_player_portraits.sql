-- Status of the cut-out character portraits fetched from NC's public profile images (one row per player that was tried).
CREATE TABLE `player_portraits` (
	`player_id` integer PRIMARY KEY NOT NULL,
	`char_key` text,
	`nc_server_id` integer,
	`status` text NOT NULL,
	`fetched_at` integer NOT NULL,
	`next_try_at` integer NOT NULL,
	FOREIGN KEY (`player_id`) REFERENCES `players`(`id`) ON UPDATE no action ON DELETE cascade
);
