CREATE TABLE `server_catalog_instances` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`server_catalog_id` integer NOT NULL,
	`instance_id` integer NOT NULL,
	FOREIGN KEY (`server_catalog_id`) REFERENCES `server_catalog`(`id`) ON UPDATE no action ON DELETE no action,
	FOREIGN KEY (`instance_id`) REFERENCES `instances`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE UNIQUE INDEX `server_catalog_instances_pair_idx` ON `server_catalog_instances` (`server_catalog_id`,`instance_id`);