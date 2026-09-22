-- The official Aion 2 regions, so the client's character picker can place an Aion 2 character on
-- one and uploads carry a server. Per the user: Europe (and NA) currently offer the eight base
-- classes only, Korea/Taiwan already have Brawler - encoded in src/constants.ts
-- SERVER_EXCLUDED_CLASSES by slug, not here. Slugs come from the post-migration backfill.
-- "version" is the region code: Aion 2 has no community patch labels the way private servers do.
INSERT OR IGNORE INTO `server_catalog` (`name`, `version`, `kind`, `game`) VALUES
	('Aion 2 Europe', 'EU', 'official', 'aion2'),
	('Aion 2 North America', 'NA', 'official', 'aion2'),
	('Aion 2 Korea', 'KR', 'official', 'aion2'),
	('Aion 2 Taiwan', 'TW', 'official', 'aion2');
