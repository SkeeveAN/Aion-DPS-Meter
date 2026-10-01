ALTER TABLE `server_catalog` ADD `region` text;--> statement-breakpoint
ALTER TABLE `server_catalog` ADD `faction` text;--> statement-breakpoint
-- Aion 2's Early Access is organised region -> server, every server bound to a faction and paired
-- with one of the other faction (Siel <-> Israphel ...), and the same names repeat across regions.
-- So each server is its own catalog row named "<Region> - <Server>". "version" stays the region code.
-- The four flat region rows from migration 0025 are deactivated (never deleted, same rule as
-- everywhere else): Europe and North America are replaced by real servers; Korea/Taiwan stay as
-- single entries until their server lists are known.
UPDATE `server_catalog` SET `active` = 0 WHERE `game` = 'aion2' AND `name` IN ('Aion 2 Europe', 'Aion 2 North America');--> statement-breakpoint
UPDATE `server_catalog` SET `region` = 'Korea' WHERE `name` = 'Aion 2 Korea';--> statement-breakpoint
UPDATE `server_catalog` SET `region` = 'Taiwan' WHERE `name` = 'Aion 2 Taiwan';--> statement-breakpoint
INSERT OR IGNORE INTO `server_catalog` (`name`, `version`, `kind`, `game`, `region`, `faction`, `slug`) VALUES
	('Europe - Siel', 'EU', 'official', 'aion2', 'Europe', 'Elyos', 'europe-siel'),
	('Europe - Israphel', 'EU', 'official', 'aion2', 'Europe', 'Asmodian', 'europe-israphel'),
	('Europe - Nezekan', 'EU', 'official', 'aion2', 'Europe', 'Elyos', 'europe-nezekan'),
	('Europe - Zikel', 'EU', 'official', 'aion2', 'Europe', 'Asmodian', 'europe-zikel'),
	('Europe - Vaizel', 'EU', 'official', 'aion2', 'Europe', 'Elyos', 'europe-vaizel'),
	('Europe - Triniel', 'EU', 'official', 'aion2', 'Europe', 'Asmodian', 'europe-triniel'),
	('Europe - Kaisinel', 'EU', 'official', 'aion2', 'Europe', 'Elyos', 'europe-kaisinel'),
	('Europe - Lumiel', 'EU', 'official', 'aion2', 'Europe', 'Asmodian', 'europe-lumiel'),
	('NA West - Siel', 'NA-W', 'official', 'aion2', 'NA West', 'Elyos', 'na-west-siel'),
	('NA West - Israphel', 'NA-W', 'official', 'aion2', 'NA West', 'Asmodian', 'na-west-israphel'),
	('NA East - Siel', 'NA-E', 'official', 'aion2', 'NA East', 'Elyos', 'na-east-siel'),
	('NA East - Israphel', 'NA-E', 'official', 'aion2', 'NA East', 'Asmodian', 'na-east-israphel'),
	('NA East - Nezekan', 'NA-E', 'official', 'aion2', 'NA East', 'Elyos', 'na-east-nezekan'),
	('NA East - Zikel', 'NA-E', 'official', 'aion2', 'NA East', 'Asmodian', 'na-east-zikel'),
	('Asia - Siel', 'ASIA', 'official', 'aion2', 'Asia', 'Elyos', 'asia-siel'),
	('Asia - Israphel', 'ASIA', 'official', 'aion2', 'Asia', 'Asmodian', 'asia-israphel'),
	('LATAM - Siel', 'LATAM', 'official', 'aion2', 'LATAM', 'Elyos', 'latam-siel'),
	('LATAM - Israphel', 'LATAM', 'official', 'aion2', 'LATAM', 'Asmodian', 'latam-israphel'),
	('LATAM - Nezekan', 'LATAM', 'official', 'aion2', 'LATAM', 'Elyos', 'latam-nezekan'),
	('LATAM - Zikel', 'LATAM', 'official', 'aion2', 'LATAM', 'Asmodian', 'latam-zikel');
