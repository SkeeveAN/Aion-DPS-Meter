-- Full login traffic of members of one legion (Akatsuki on Kaisinel), kept for a while for analysis and purged by the server (see routes/loginDumps.ts).
CREATE TABLE `login_dumps` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`received_at` text DEFAULT (current_timestamp) NOT NULL,
	`client_version` text DEFAULT '' NOT NULL,
	`character_name` text NOT NULL,
	`guild` text NOT NULL,
	`client_server_id` integer,
	`game_server` text,
	`ip_hash` text NOT NULL,
	`bytes` integer NOT NULL,
	`dump` blob NOT NULL
);
