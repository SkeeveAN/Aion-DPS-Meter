-- Damage of a participant estimated from hit counts (crowded world boss), kept out of every ranking.
ALTER TABLE `encounter_participants` ADD `estimated` integer DEFAULT 0 NOT NULL;
