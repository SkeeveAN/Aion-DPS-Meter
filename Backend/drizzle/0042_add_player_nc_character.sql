-- Character data (attributes, wing, wing skin, active pet) from NC's official character page, one row per player that was tried.
CREATE TABLE `player_nc_character` (
	`player_id` integer PRIMARY KEY NOT NULL,
	`character_id` text,
	`nc_server_id` integer,
	`attributes_json` text DEFAULT '{}' NOT NULL,
	`wing_id` integer,
	`wing_skin_id` integer,
	`active_pet` integer,
	`active_pet_level` integer,
	`status` text NOT NULL,
	`fetched_at` integer NOT NULL,
	`next_try_at` integer NOT NULL,
	FOREIGN KEY (`player_id`) REFERENCES `players`(`id`) ON UPDATE no action ON DELETE cascade
);
