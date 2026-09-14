-- Found while researching images: aion.fandom.com's own Ophidan Bridge/Jormungand's Bridge page
-- names this boss "Escapee Asachin", not "Fugitive Asachin" (this app's own name from migration
-- 0016, taken from the user's original, unverified report). No real encounters reference this row
-- yet, so a plain rename is safe.
UPDATE `bosses` SET `name` = 'Escapee Asachin' WHERE `name` = 'Fugitive Asachin';
