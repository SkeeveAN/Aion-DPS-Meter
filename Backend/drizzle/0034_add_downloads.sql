-- One row per click on the site's download button (GET /download/latest): counts downloads that came through the website; the client's own auto-update downloads from GitHub directly and is not in here.
CREATE TABLE `downloads` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`downloaded_at` text DEFAULT (current_timestamp) NOT NULL,
	`ip_hash` text NOT NULL,
	`tag` text NOT NULL,
	`is_bot` integer DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE INDEX `downloads_downloaded_at_idx` ON `downloads` (`downloaded_at`);
