-- Narrow indexes for the statistics and /metrics queries: they used to scan the wide rows of uploads (563 MB, mostly payloads) and of player_profiles (244 MB).
CREATE INDEX `uploads_received_at_ip_idx` ON `uploads` (`received_at`,`ip_hash`);--> statement-breakpoint
CREATE INDEX `uploads_status_idx` ON `uploads` (`status`);--> statement-breakpoint
CREATE INDEX `uploads_server_status_ip_idx` ON `uploads` (`server_id`,`status`,`ip_hash`);--> statement-breakpoint
CREATE INDEX `uploads_game_server_idx` ON `uploads` (`game_server`,`client_server_id`) WHERE game_server is not null;--> statement-breakpoint
CREATE INDEX `player_profiles_source_idx` ON `player_profiles` (`source`);
