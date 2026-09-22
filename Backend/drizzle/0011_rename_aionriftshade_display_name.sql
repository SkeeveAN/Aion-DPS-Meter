-- "AionRiftshade" (no space) was a typo in 0010 - the real display name per the user is
-- "Aion Riftshade". No real `servers` row can reference it by this name yet (see servers.ts's own
-- remarks - it only matches by exact displayName string), so a plain rename is safe.
UPDATE `server_catalog` SET `name` = 'Aion Riftshade' WHERE `name` = 'AionRiftshade';
