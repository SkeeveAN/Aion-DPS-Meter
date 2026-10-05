-- Group shields (Chanter / Templar): damage a participant's shields soaked up and, for the caster, who got how much.
-- Both stay empty until the client reports them.
ALTER TABLE `encounter_participants` ADD `damage_absorbed` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounter_participants` ADD `shields_given` text DEFAULT '[]' NOT NULL;
