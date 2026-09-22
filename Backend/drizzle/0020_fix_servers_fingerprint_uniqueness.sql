DROP INDEX IF EXISTS `servers_fingerprint_unique`;--> statement-breakpoint
CREATE UNIQUE INDEX `servers_fingerprint_display_name_idx` ON `servers` (`fingerprint`,`display_name`);