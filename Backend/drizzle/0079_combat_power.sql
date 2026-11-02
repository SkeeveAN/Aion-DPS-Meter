-- The combat power a player had during the fight (the party roster's value, sent by clients from 1.8 on). Null = not reported.
ALTER TABLE `encounter_participants` ADD `combat_power` integer;
