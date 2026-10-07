-- The number the client read as "combat power" is the gear score (Ausrüstungswert) the character window shows beside the helmet icon.
ALTER TABLE `player_profiles` RENAME COLUMN `combat_power` TO `gear_score`;
