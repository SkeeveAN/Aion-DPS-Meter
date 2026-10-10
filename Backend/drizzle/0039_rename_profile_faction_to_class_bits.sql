-- What the client sent as the profile's "faction" is the low two bits of the class code (4 * class id + bits), not a faction:
-- Elyos characters carry 1 and 2 there. The real faction comes from a fight (encounter_participants.faction).
ALTER TABLE `player_profiles` RENAME COLUMN `faction` TO `class_bits`;
