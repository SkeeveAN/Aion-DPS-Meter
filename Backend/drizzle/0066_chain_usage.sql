-- Chain skills of a participant, worked out when the time axis is stored (chainUsage.ts): per skill and follow-up, how many uses were followed by it in time.
CREATE TABLE `encounter_chain_usage` (
	`participant_id` integer NOT NULL,
	`skill_id` integer NOT NULL,
	`next_id` integer NOT NULL,
	`offered` integer NOT NULL,
	`taken` integer NOT NULL,
	PRIMARY KEY(`participant_id`, `skill_id`, `next_id`),
	FOREIGN KEY (`participant_id`) REFERENCES `encounter_participants`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
-- 1 = the chain rows of this time axis are filled in (a participant without chain skills has no row, so the job needs this mark)
ALTER TABLE `encounter_timelines` ADD `chains_done` integer DEFAULT 0 NOT NULL;
