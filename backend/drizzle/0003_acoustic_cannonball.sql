CREATE TABLE `server_catalog` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`name` text NOT NULL,
	`version` text NOT NULL,
	`kind` text NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `server_catalog_name_unique` ON `server_catalog` (`name`);
--> statement-breakpoint
-- Researched against each server's own site/forum and current private-server rankings (gtop100,
-- nostalgic.gg, aion.bestgames.to) in September 2026 - not guessed. Deliberately not exhaustive
-- (dozens of small private servers exist and churn constantly); this covers the official Gameforge
-- release plus the private servers that were actually verifiable with a real patch version at the
-- time of writing. "Origin Aion" is this app's own home server per the user's earlier confirmation.
INSERT INTO `server_catalog` (`name`, `version`, `kind`) VALUES
	('AION Classic EU (Gameforge)', '4.5', 'official'),
	('Origin Aion', '4.6', 'private'),
	('EuroAion', '4.6', 'private'),
	('AlfaAion', '4.6.2', 'private'),
	('Elden Aion', '3.9', 'private'),
	('Aion America - Fall of Beritra', '4.8', 'private'),
	('Next Aion', '1.9', 'private'),
	('Aion Reborn', '5.8', 'private'),
	('AionPT', '5.8', 'private'),
	('World Of Aion', '3.0', 'private'),
	('InfiniteAION', '7.7', 'private'),
	('HiveGamez Aion Classic', '1.2-2.5', 'private'),
	('NOVArpg', '1.2', 'private');