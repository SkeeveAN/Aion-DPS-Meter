-- Three more instances from the user's own 4.8 instance list that already had pre-staged
-- translations/images (app.js/i18n.js) but, unlike Sauro/Katalamize/etc., turned out to have no
-- real `instances` row in production at all yet - confirmed missing by querying the live DB
-- directly (`SELECT ... FROM instances WHERE name IN (...)` returned nothing) before writing this,
-- same for their bosses by name across ALL instances, to avoid repeating the Ophidan Bridge
-- duplicate-row mistake from migration 0010.
--
-- "Tiamats Unterschlupf" (not "Tiamats Festung", the OTHER already-pre-staged Tiamat instance) is
-- specifically "Dragon Lord's Refuge" per aionriftshade.com's own client (mapCode IDTiamat_2, see
-- assets/places/instances_multilang.json) - "Tiamats Festung"/IDTiamat_1 is a separate zone,
-- "Tiamat Stronghold", not requested here and left alone.
INSERT OR IGNORE INTO `instances` (`name`, `sort_order`) VALUES
	('Beshmundirs Tempel', 0),
	('Rentus-Basis', 0),
	('Tiamats Unterschlupf', 0);
--> statement-breakpoint

INSERT INTO `bosses` (`instance_id`, `name`) VALUES
	((SELECT id FROM instances WHERE name = 'Beshmundirs Tempel'), 'Stormwing'),
	((SELECT id FROM instances WHERE name = 'Rentus-Basis'), 'Brigade General Vasharti'),
	((SELECT id FROM instances WHERE name = 'Tiamats Unterschlupf'), 'Tiamat');
