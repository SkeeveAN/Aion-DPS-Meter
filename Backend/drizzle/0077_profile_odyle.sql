-- The own Odyle energy ({count, bonus, complete, at, stacks: [{item, quantity, place, expires, unit}]}): the energy now and the Odyle energy items in the inventory and storages; own character only.
ALTER TABLE `player_profiles` ADD `odyle_json` text DEFAULT '{}' NOT NULL;
