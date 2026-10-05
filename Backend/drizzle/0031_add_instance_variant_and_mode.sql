-- Instances the EU client does not offer are hidden, not deleted; expedition difficulty (normal / hard) is its own instance.
ALTER TABLE `instances` ADD `hidden` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `instances` ADD `variant` text;
--> statement-breakpoint
-- Difficulty step inside one boss (Nightmare stage 1-10, Ascension difficulty, Transcendence stage 1-4).
ALTER TABLE `encounters` ADD `mode` text DEFAULT '' NOT NULL;
