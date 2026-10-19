-- What a player's stat calculator recorded and chose to send, and the network reports a player chose to send. Both are linked to nothing else; no name, no legion, no IP address (see routes/statRecordings.ts, routes/networkReports.ts).
CREATE TABLE `stat_recordings` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`received_at` text DEFAULT (current_timestamp) NOT NULL,
	`client_version` text DEFAULT '' NOT NULL,
	`anon_id` text NOT NULL,
	`class_id` integer,
	`level` integer,
	`server_id` integer,
	`faction` text,
	`mode` text DEFAULT 'simple' NOT NULL,
	`duration_seconds` integer DEFAULT 0 NOT NULL,
	`bytes` integer NOT NULL,
	`payload` blob NOT NULL
);
--> statement-breakpoint
CREATE TABLE `network_reports` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`received_at` text DEFAULT (current_timestamp) NOT NULL,
	`client_version` text DEFAULT '' NOT NULL,
	`anon_id` text NOT NULL,
	`period_seconds` integer DEFAULT 0 NOT NULL,
	`stall_count` integer DEFAULT 0 NOT NULL,
	`bytes` integer NOT NULL,
	`payload` blob NOT NULL
);
