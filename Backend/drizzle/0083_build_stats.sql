-- "What do the best use?": answers per class, ranking and group (Top 10, Top 25, Top 10 %, all), rebuilt as a whole by buildStats.ts (SQLite has no materialized views, so this table is the
-- stored result and the job is the refresh). Counts only, never a single character; cells that fewer than three characters share are not stored.
CREATE TABLE `build_stats` (
	`class_id` integer NOT NULL,
	`metric` text NOT NULL,
	`grp` text NOT NULL,
	`topic` text NOT NULL,
	`key` text NOT NULL,
	`option` text NOT NULL,
	`n` integer NOT NULL,
	`w` real NOT NULL,
	`base_n` integer NOT NULL,
	`base_w` real NOT NULL,
	`detail` text,
	PRIMARY KEY(`class_id`, `metric`, `grp`, `topic`, `key`, `option`)
) WITHOUT ROWID;--> statement-breakpoint
CREATE TABLE `build_stats_meta` (
	`class_id` integer NOT NULL,
	`metric` text NOT NULL,
	`grp` text NOT NULL,
	`size` integer NOT NULL,
	`weight` real NOT NULL,
	`built_at` text NOT NULL,
	PRIMARY KEY(`class_id`, `metric`, `grp`)
) WITHOUT ROWID;
